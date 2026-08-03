using Microsoft.Data.Sqlite;
using SoccerSim.Content;
using SoccerSim.Content.Model;
using SoccerSim.Content.Validation;

namespace SoccerSim.Infrastructure.Sqlite.Content;

/// <summary>
/// Writes an authored <see cref="ContentBundle"/> into a migrated SQLite database.
///
/// The bundle is validated again here even though the authoring tool already validated it —
/// a bundle can reach the game by any route (a hand-edited JSON file, a downloaded build), and
/// the alternative to failing loudly is a half-populated world and an opaque constraint
/// violation somewhere much later.
/// </summary>
public sealed class SqliteContentImporter
{
    private readonly SqliteConnection _connection;

    public SqliteContentImporter(SqliteConnection connection)
        => _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>
    /// Imports the bundle unless this database already holds a build with the same content hash,
    /// in which case it is a no-op. Returns what was (or already had been) imported.
    /// </summary>
    public ContentImportReport EnsureImported(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        ContentBuildRecord? existing = ContentBuildInfo.Read(_connection);
        if (existing is not null
            && string.Equals(existing.ContentHash, bundle.Manifest.ContentHash, StringComparison.Ordinal))
        {
            return new ContentImportReport(bundle.Manifest, bundle.CountByCategory(), AlreadyPresent: true);
        }

        if (existing is not null)
        {
            // Re-importing different content over a live save would renumber rows that Matches,
            // Standings and Career already point at. Replacing content in place is a Phase 4
            // concern with its own merge rules; refusing is the honest behaviour until then.
            throw new ContentBuildMismatchException(
                existing.ContentHash,
                bundle.Manifest.ContentHash,
                _connection.DataSource,
                $"The database '{_connection.DataSource}' already holds content build "
                + $"'{existing.BuildId}' ({existing.ContentHash}). Importing a different build "
                + $"('{bundle.Manifest.ContentHash}') over an existing save is not supported, because "
                + "the ids that Matches, Standings and Career point at would be renumbered. "
                + "Delete that file to start a new save from the current content.");
        }

        ContentValidator.Default.Validate(bundle).ThrowIfInvalid();

        using SqliteTransaction transaction = _connection.BeginTransaction();

        // Order matters: a table's foreign keys must already resolve when its rows go in.
        InsertNations(bundle, transaction);
        InsertStadiums(bundle, transaction);
        InsertCompetitions(bundle, transaction);
        InsertTraits(bundle, transaction);
        InsertLeagues(bundle, transaction);
        InsertTeams(bundle, transaction);
        InsertPlayers(bundle, transaction);
        InsertCoaches(bundle, transaction);
        InsertContracts(bundle, transaction);
        InsertHousingItems(bundle, transaction);
        InsertWorld(bundle, transaction);

        ContentBuildInfo.Write(_connection, transaction, bundle.Manifest);
        transaction.Commit();

        return new ContentImportReport(bundle.Manifest, bundle.CountByCategory(), AlreadyPresent: false);
    }

    private void InsertNations(ContentBundle bundle, SqliteTransaction transaction)
    {
        foreach (ContentNation nation in bundle.Nations)
        {
            Execute(transaction,
                "INSERT INTO Nations (Id, Key, Name, Code, Adjective, Confederation, Reputation) "
                + "VALUES ($id, $key, $name, $code, $adj, $conf, $rep);",
                ("$id", nation.Id),
                ("$key", nation.Key),
                ("$name", nation.Name),
                ("$code", nation.Code),
                ("$adj", (object?)nation.Adjective ?? DBNull.Value),
                ("$conf", (object?)nation.Confederation ?? DBNull.Value),
                ("$rep", nation.Reputation));
        }
    }

    private void InsertStadiums(ContentBundle bundle, SqliteTransaction transaction)
    {
        var nationIds = Ids(bundle.Nations);

        foreach (ContentStadium stadium in bundle.Stadiums)
        {
            Execute(transaction,
                "INSERT INTO Stadiums (Id, Key, Name, NationId, City, Capacity, PitchLengthM, "
                + "PitchWidthM, Surface, YearBuilt) "
                + "VALUES ($id, $key, $name, $nation, $city, $cap, $len, $wid, $surface, $built);",
                ("$id", stadium.Id),
                ("$key", stadium.Key),
                ("$name", stadium.Name),
                ("$nation", Lookup(nationIds, stadium.NationKey)),
                ("$city", (object?)stadium.City ?? DBNull.Value),
                ("$cap", stadium.Capacity),
                ("$len", stadium.PitchLengthM),
                ("$wid", stadium.PitchWidthM),
                ("$surface", stadium.Surface.ToString().ToLowerInvariant()),
                ("$built", (object?)stadium.YearBuilt ?? DBNull.Value));
        }
    }

    private void InsertCompetitions(ContentBundle bundle, SqliteTransaction transaction)
    {
        var nationIds = Ids(bundle.Nations);

        foreach (ContentCompetition competition in bundle.Competitions)
        {
            Execute(transaction,
                "INSERT INTO Competitions (Id, Key, Name, ShortName, NationId, Format, Scope, "
                + "Reputation, PointsWin, PointsDraw) "
                + "VALUES ($id, $key, $name, $short, $nation, $format, $scope, $rep, $win, $draw);",
                ("$id", competition.Id),
                ("$key", competition.Key),
                ("$name", competition.Name),
                ("$short", (object?)competition.ShortName ?? DBNull.Value),
                ("$nation", Lookup(nationIds, competition.NationKey)),
                ("$format", competition.Format.ToString().ToLowerInvariant()),
                ("$scope", competition.Scope.ToString().ToLowerInvariant()),
                ("$rep", competition.Reputation),
                ("$win", competition.PointsWin),
                ("$draw", competition.PointsDraw));
        }
    }

    private void InsertCoaches(ContentBundle bundle, SqliteTransaction transaction)
    {
        var nationIds = Ids(bundle.Nations);
        var teamIds = Ids(bundle.Teams);

        foreach (ContentCoach coach in bundle.Coaches)
        {
            Execute(transaction,
                "INSERT INTO Coaches (Id, Key, FirstName, LastName, TeamId, NationId, Role, "
                + "DateOfBirth, Coaching, TacticalKnowledge, ManManagement, Fitness, Scouting, "
                + "PreferredMentality) "
                + "VALUES ($id, $key, $first, $last, $team, $nation, $role, $dob, $coaching, "
                + "$tactical, $manmgmt, $fitness, $scouting, $mentality);",
                ("$id", coach.Id),
                ("$key", coach.Key),
                ("$first", coach.FirstName),
                ("$last", coach.LastName),
                ("$team", Lookup(teamIds, coach.TeamKey)),
                ("$nation", Lookup(nationIds, coach.NationKey)),
                ("$role", SnakeCase(coach.Role.ToString())),
                ("$dob", coach.DateOfBirth is null ? DBNull.Value : SqliteValue.ToText(coach.DateOfBirth.Value)),
                ("$coaching", coach.Coaching),
                ("$tactical", coach.TacticalKnowledge),
                ("$manmgmt", coach.ManManagement),
                ("$fitness", coach.Fitness),
                ("$scouting", coach.Scouting),
                ("$mentality", coach.PreferredMentality));
        }
    }

    private void InsertContracts(ContentBundle bundle, SqliteTransaction transaction)
    {
        var playerIds = Ids(bundle.Players);
        var coachIds = Ids(bundle.Coaches);
        var teamIds = Ids(bundle.Teams);

        foreach (ContentContract contract in bundle.Contracts)
        {
            Execute(transaction,
                "INSERT INTO Contracts (Id, Key, PlayerId, CoachId, TeamId, StartDate, EndDate, "
                + "WeeklyWage, SigningBonus, ReleaseClause, SquadStatus) "
                + "VALUES ($id, $key, $player, $coach, $team, $start, $end, $wage, $bonus, "
                + "$clause, $status);",
                ("$id", contract.Id),
                ("$key", contract.Key),
                ("$player", Lookup(playerIds, contract.PlayerKey)),
                ("$coach", Lookup(coachIds, contract.CoachKey)),
                ("$team", teamIds[contract.TeamKey]),
                ("$start", SqliteValue.ToText(contract.StartDate)),
                ("$end", SqliteValue.ToText(contract.EndDate)),
                ("$wage", contract.WeeklyWage),
                ("$bonus", contract.SigningBonus),
                ("$clause", (object?)contract.ReleaseClause ?? DBNull.Value),
                ("$status", SnakeCase(contract.SquadStatus.ToString())));
        }
    }

    private void InsertTraits(ContentBundle bundle, SqliteTransaction transaction)
    {
        foreach (ContentTrait trait in bundle.Traits)
        {
            Execute(transaction,
                "INSERT INTO PlayerTraits (Id, Key, DisplayName, Aggression, Selfishness, EventWeightBias) "
                + "VALUES ($id, $key, $name, $agg, $self, $bias);",
                ("$id", trait.Id),
                ("$key", trait.Key),
                ("$name", trait.DisplayName),
                ("$agg", trait.Aggression),
                ("$self", trait.Selfishness),
                ("$bias", trait.EventWeightBias));
        }
    }

    private void InsertLeagues(ContentBundle bundle, SqliteTransaction transaction)
    {
        var nationIds = Ids(bundle.Nations);
        var competitionIds = Ids(bundle.Competitions);

        foreach (ContentLeague league in bundle.Leagues)
        {
            // CurrentSeasonId is a soft pointer with no FK; it is set after the seasons exist.
            Execute(transaction,
                "INSERT INTO Leagues (Id, Key, Name, Country, Tier, CurrentSeasonId, CompetitionId, "
                + "NationId, PyramidLevel, PromotionSlots, RelegationSlots) "
                + "VALUES ($id, $key, $name, $country, $tier, NULL, $competition, $nation, "
                + "$level, $promotion, $relegation);",
                ("$id", league.Id),
                ("$key", league.Key),
                ("$name", league.Name),
                ("$country", league.Country),
                ("$tier", (int)league.Tier),
                ("$competition", Lookup(competitionIds, league.CompetitionKey)),
                ("$nation", Lookup(nationIds, league.NationKey)),
                ("$level", league.PyramidLevel),
                ("$promotion", league.PromotionSlots),
                ("$relegation", league.RelegationSlots));
        }
    }

    private void InsertTeams(ContentBundle bundle, SqliteTransaction transaction)
    {
        var leagueIds = Ids(bundle.Leagues);
        var nationIds = Ids(bundle.Nations);
        var stadiumIds = Ids(bundle.Stadiums);

        foreach (ContentTeam team in bundle.Teams)
        {
            Execute(transaction,
                "INSERT INTO Teams (Id, Key, Name, LeagueId, Budget, EloRating, ShortName, "
                + "NationId, StadiumId, FoundedYear, Reputation) "
                + "VALUES ($id, $key, $name, $league, $budget, $elo, $short, $nation, $stadium, "
                + "$founded, $rep);",
                ("$id", team.Id),
                ("$key", team.Key),
                ("$name", team.Name),
                ("$league", leagueIds[team.LeagueKey]),
                ("$budget", team.Budget),
                ("$elo", team.EloRating),
                ("$short", (object?)team.ShortName ?? DBNull.Value),
                ("$nation", Lookup(nationIds, team.NationKey)),
                ("$stadium", Lookup(stadiumIds, team.StadiumKey)),
                ("$founded", (object?)team.FoundedYear ?? DBNull.Value),
                ("$rep", team.Reputation));
        }
    }

    private void InsertPlayers(ContentBundle bundle, SqliteTransaction transaction)
    {
        var teamIds = Ids(bundle.Teams);
        var traitIds = Ids(bundle.Traits);
        var nationIds = Ids(bundle.Nations);

        foreach (ContentPlayer player in bundle.Players)
        {
            ContentAttributes a = player.Attributes;
            Execute(transaction,
                "INSERT INTO Players (Id, Key, FirstName, LastName, TeamId, Pace, Stamina, Strength, "
                + "Passing, Shooting, Tackling, Vision, DateOfBirth, NationId, PreferredFoot, "
                + "PrimaryRole, Flank, SquadNumber, HeightCm) "
                + "VALUES ($id, $key, $first, $last, $team, $pace, $stamina, $strength, $passing, "
                + "$shooting, $tackling, $vision, $dob, $nation, $foot, $role, $flank, $number, $height);",
                ("$id", player.Id),
                ("$key", player.Key),
                ("$first", player.FirstName),
                ("$last", player.LastName),
                ("$team", Lookup(teamIds, player.TeamKey)),
                ("$pace", a.Pace),
                ("$stamina", a.Stamina),
                ("$strength", a.Strength),
                ("$passing", a.Passing),
                ("$shooting", a.Shooting),
                ("$tackling", a.Tackling),
                ("$vision", a.Vision),
                ("$dob", player.DateOfBirth is null ? DBNull.Value : SqliteValue.ToText(player.DateOfBirth.Value)),
                ("$nation", Lookup(nationIds, player.NationKey)),
                ("$foot", player.PreferredFoot.ToString()),
                ("$role", player.PrimaryRole.ToString()),
                ("$flank", player.Flank.ToString()),
                ("$number", (object?)player.SquadNumber ?? DBNull.Value),
                ("$height", (object?)player.HeightCm ?? DBNull.Value));

            foreach (string traitKey in player.TraitKeys)
            {
                Execute(transaction,
                    "INSERT INTO PlayerTraitAssignments (PlayerId, TraitId) VALUES ($player, $trait);",
                    ("$player", player.Id),
                    ("$trait", traitIds[traitKey]));
            }
        }
    }

    private void InsertHousingItems(ContentBundle bundle, SqliteTransaction transaction)
    {
        foreach (ContentHousingItem item in bundle.HousingItems)
        {
            Execute(transaction,
                "INSERT INTO HousingItems (Id, Key, Name, Cost, StatKey, YieldMultiplier) "
                + "VALUES ($id, $key, $name, $cost, $stat, $yield);",
                ("$id", item.Id),
                ("$key", item.Key),
                ("$name", item.Name),
                ("$cost", item.Cost),
                ("$stat", item.StatKey),
                ("$yield", item.YieldMultiplier));
        }
    }

    private void InsertWorld(ContentBundle bundle, SqliteTransaction transaction)
    {
        ContentWorld world = bundle.World;
        var leagueIds = bundle.Leagues.ToDictionary(l => l.Key, l => l.Id, StringComparer.Ordinal);
        var teamIds = bundle.Teams.ToDictionary(t => t.Key, t => t.Id, StringComparer.Ordinal);
        var playerIds = bundle.Players.ToDictionary(p => p.Key, p => p.Id, StringComparer.Ordinal);
        var seasonIds = world.Seasons.ToDictionary(s => s.Key, s => s.Id, StringComparer.Ordinal);

        foreach (ContentSeason season in world.Seasons)
        {
            Execute(transaction,
                "INSERT INTO Seasons (Id, Key, LeagueId, StartDate, EndDate) "
                + "VALUES ($id, $key, $league, $start, $end);",
                ("$id", season.Id),
                ("$key", season.Key),
                ("$league", leagueIds[season.LeagueKey]),
                ("$start", SqliteValue.ToText(season.StartDate)),
                ("$end", SqliteValue.ToText(season.EndDate)));

            if (season.IsCurrent)
            {
                Execute(transaction,
                    "UPDATE Leagues SET CurrentSeasonId = $season WHERE Id = $league;",
                    ("$season", season.Id),
                    ("$league", leagueIds[season.LeagueKey]));
            }
        }

        foreach (ContentFixture fixture in world.Fixtures)
        {
            int seasonId = seasonIds[fixture.SeasonKey];
            ContentSeason season = world.Seasons.First(s =>
                string.Equals(s.Key, fixture.SeasonKey, StringComparison.Ordinal));

            Execute(transaction,
                "INSERT INTO Matches (Id, Key, SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played) "
                + "VALUES ($id, $key, $season, $league, $home, $away, $kickoff, 0);",
                ("$id", fixture.Id),
                ("$key", fixture.Key),
                ("$season", seasonId),
                ("$league", leagueIds[season.LeagueKey]),
                ("$home", teamIds[fixture.HomeTeamKey]),
                ("$away", teamIds[fixture.AwayTeamKey]),
                ("$kickoff", SqliteValue.ToText(fixture.KickoffDate)));
        }

        foreach (ContentPlayerFinance finance in world.Finances)
        {
            Execute(transaction,
                "INSERT INTO PlayerFinances (PlayerId, Balance, BaseSalaryWeekly) "
                + "VALUES ($player, $balance, $salary);",
                ("$player", playerIds[finance.PlayerKey]),
                ("$balance", finance.Balance),
                ("$salary", finance.BaseSalaryWeekly));
        }

        if (world.HumanPlayerKey is not null)
        {
            Execute(transaction,
                "INSERT INTO Career (Id, HumanPlayerId) VALUES (1, $player);",
                ("$player", playerIds[world.HumanPlayerKey]));
        }
    }

    private static Dictionary<string, int> Ids<T>(IReadOnlyList<T> entities) where T : IContentEntity
        => entities.ToDictionary(e => e.Key, e => e.Id, StringComparer.Ordinal);

    /// <summary>Optional foreign key: a null key becomes SQL NULL rather than a lookup failure.</summary>
    private static object Lookup(Dictionary<string, int> ids, string? key)
        => key is null ? DBNull.Value : ids[key];

    /// <summary>
    /// PascalCase enum name to the snake_case the CHECK constraints use
    /// (<c>GoalkeepingCoach</c> → <c>goalkeeping_coach</c>).
    /// </summary>
    private static string SnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    private void Execute(SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// Thrown when a database already holds a different content build. Distinct from a plain
/// <see cref="InvalidOperationException"/> so the authoring loop can recognise exactly this
/// case — content edited, save stale — and rebuild the file instead of failing the boot.
/// </summary>
public sealed class ContentBuildMismatchException : InvalidOperationException
{
    public ContentBuildMismatchException(
        string existingContentHash,
        string incomingContentHash,
        string databasePath,
        string message)
        : base(message)
    {
        ExistingContentHash = existingContentHash;
        IncomingContentHash = incomingContentHash;
        DatabasePath = databasePath;
    }

    /// <summary>The content hash the database was populated from.</summary>
    public string ExistingContentHash { get; }

    /// <summary>The content hash of the bundle that was refused.</summary>
    public string IncomingContentHash { get; }

    /// <summary>The file to delete to start over, as SQLite knows it.</summary>
    public string DatabasePath { get; }
}

/// <summary>What an import did, for the boot log and the tool's summary.</summary>
public sealed record ContentImportReport(
    ContentManifest Manifest,
    IReadOnlyDictionary<string, int> Counts,
    bool AlreadyPresent)
{
    public string Describe()
    {
        string counts = string.Join(", ", Counts.Where(c => c.Value > 0).Select(c => $"{c.Value} {c.Key}"));
        string verb = AlreadyPresent ? "already present" : "imported";
        return $"content build {Manifest.BuildId} ({Manifest.ContentHash}) {verb}: {counts}";
    }
}
