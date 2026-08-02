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
            throw new InvalidOperationException(
                $"This database already holds content build '{existing.BuildId}' ({existing.ContentHash}). "
                + $"Importing a different build ('{bundle.Manifest.ContentHash}') over an existing save is "
                + "not supported — start a new save instead.");
        }

        ContentValidator.Default.Validate(bundle).ThrowIfInvalid();

        using SqliteTransaction transaction = _connection.BeginTransaction();

        InsertTraits(bundle, transaction);
        InsertLeagues(bundle, transaction);
        InsertTeams(bundle, transaction);
        InsertPlayers(bundle, transaction);
        InsertHousingItems(bundle, transaction);
        InsertWorld(bundle, transaction);

        ContentBuildInfo.Write(_connection, transaction, bundle.Manifest);
        transaction.Commit();

        return new ContentImportReport(bundle.Manifest, bundle.CountByCategory(), AlreadyPresent: false);
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
        foreach (ContentLeague league in bundle.Leagues)
        {
            // CurrentSeasonId is a soft pointer with no FK; it is set after the seasons exist.
            Execute(transaction,
                "INSERT INTO Leagues (Id, Key, Name, Country, Tier, CurrentSeasonId) "
                + "VALUES ($id, $key, $name, $country, $tier, NULL);",
                ("$id", league.Id),
                ("$key", league.Key),
                ("$name", league.Name),
                ("$country", league.Country),
                ("$tier", (int)league.Tier));
        }
    }

    private void InsertTeams(ContentBundle bundle, SqliteTransaction transaction)
    {
        var leagueIds = bundle.Leagues.ToDictionary(l => l.Key, l => l.Id, StringComparer.Ordinal);

        foreach (ContentTeam team in bundle.Teams)
        {
            Execute(transaction,
                "INSERT INTO Teams (Id, Key, Name, LeagueId, Budget, EloRating) "
                + "VALUES ($id, $key, $name, $league, $budget, $elo);",
                ("$id", team.Id),
                ("$key", team.Key),
                ("$name", team.Name),
                ("$league", leagueIds[team.LeagueKey]),
                ("$budget", team.Budget),
                ("$elo", team.EloRating));
        }
    }

    private void InsertPlayers(ContentBundle bundle, SqliteTransaction transaction)
    {
        var teamIds = bundle.Teams.ToDictionary(t => t.Key, t => t.Id, StringComparer.Ordinal);
        var traitIds = bundle.Traits.ToDictionary(t => t.Key, t => t.Id, StringComparer.Ordinal);

        foreach (ContentPlayer player in bundle.Players)
        {
            ContentAttributes a = player.Attributes;
            Execute(transaction,
                "INSERT INTO Players (Id, Key, FirstName, LastName, TeamId, Pace, Stamina, Strength, "
                + "Passing, Shooting, Tackling, Vision) "
                + "VALUES ($id, $key, $first, $last, $team, $pace, $stamina, $strength, $passing, "
                + "$shooting, $tackling, $vision);",
                ("$id", player.Id),
                ("$key", player.Key),
                ("$first", player.FirstName),
                ("$last", player.LastName),
                ("$team", player.TeamKey is null ? DBNull.Value : teamIds[player.TeamKey]),
                ("$pace", a.Pace),
                ("$stamina", a.Stamina),
                ("$strength", a.Strength),
                ("$passing", a.Passing),
                ("$shooting", a.Shooting),
                ("$tackling", a.Tackling),
                ("$vision", a.Vision));

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
