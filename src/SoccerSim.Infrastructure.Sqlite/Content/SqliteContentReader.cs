using Microsoft.Data.Sqlite;
using SoccerSim.Content;
using SoccerSim.Content.Model;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Infrastructure.Sqlite.Content;

/// <summary>
/// Reads authored rows back out of SQLite into a <see cref="ContentBundle"/> — the inverse of
/// <see cref="SqliteContentImporter"/>.
///
/// This is what makes the authoring tool possible: edits are made against a working database,
/// and export is just "read it back and write JSON". It also gives the round-trip test that
/// proves the column mapping is lossless in both directions.
/// </summary>
public sealed class SqliteContentReader
{
    private readonly SqliteConnection _connection;

    public SqliteContentReader(SqliteConnection connection)
        => _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public ContentBundle Read()
    {
        List<ContentNation> nations = ReadNations();
        var nationKeys = nations.ToDictionary(n => n.Id, n => n.Key);
        List<ContentStadium> stadiums = ReadStadiums(nationKeys);
        var stadiumKeys = stadiums.ToDictionary(s => s.Id, s => s.Key);
        List<ContentCompetition> competitions = ReadCompetitions(nationKeys);
        var competitionKeys = competitions.ToDictionary(c => c.Id, c => c.Key);

        List<ContentTrait> traits = ReadTraits();
        List<ContentLeague> leagues = ReadLeagues(competitionKeys, nationKeys);
        List<ContentTeam> teams = ReadTeams(leagues, nationKeys, stadiumKeys);
        List<ContentPlayer> players = ReadPlayers(teams, traits, nationKeys);
        List<ContentCoach> coaches = ReadCoaches(teams, nationKeys);

        return new ContentBundle
        {
            Manifest = ReadManifest(),
            Nations = nations,
            Stadiums = stadiums,
            Competitions = competitions,
            Traits = traits,
            Leagues = leagues,
            Teams = teams,
            Players = players,
            Coaches = coaches,
            Contracts = ReadContracts(players, coaches, teams),
            HousingItems = ReadHousingItems(),
            World = ReadWorld(leagues, teams, players),
        };
    }

    private List<ContentNation> ReadNations() => QueryAll(
        "SELECT Id, Key, Name, Code, Adjective, Confederation, Reputation FROM Nations ORDER BY Id;",
        reader => new ContentNation
        {
            Id = reader.GetInt32(0),
            Key = reader.GetString(1),
            Name = reader.GetString(2),
            Code = reader.GetString(3),
            Adjective = reader.IsDBNull(4) ? null : reader.GetString(4),
            Confederation = reader.IsDBNull(5) ? null : reader.GetString(5),
            Reputation = reader.GetInt32(6),
        });

    private List<ContentStadium> ReadStadiums(Dictionary<int, string> nationKeys) => QueryAll(
        "SELECT Id, Key, Name, NationId, City, Capacity, PitchLengthM, PitchWidthM, Surface, "
        + "YearBuilt FROM Stadiums ORDER BY Id;",
        reader => new ContentStadium
        {
            Id = reader.GetInt32(0),
            Key = reader.GetString(1),
            Name = reader.GetString(2),
            NationKey = reader.IsDBNull(3) ? null : nationKeys[reader.GetInt32(3)],
            City = reader.IsDBNull(4) ? null : reader.GetString(4),
            Capacity = reader.GetInt32(5),
            PitchLengthM = reader.GetInt32(6),
            PitchWidthM = reader.GetInt32(7),
            Surface = ParseEnum<PitchSurface>(reader.GetString(8)),
            YearBuilt = reader.IsDBNull(9) ? null : reader.GetInt32(9),
        });

    private List<ContentCompetition> ReadCompetitions(Dictionary<int, string> nationKeys) => QueryAll(
        "SELECT Id, Key, Name, ShortName, NationId, Format, Scope, Reputation, PointsWin, "
        + "PointsDraw FROM Competitions ORDER BY Id;",
        reader => new ContentCompetition
        {
            Id = reader.GetInt32(0),
            Key = reader.GetString(1),
            Name = reader.GetString(2),
            ShortName = reader.IsDBNull(3) ? null : reader.GetString(3),
            NationKey = reader.IsDBNull(4) ? null : nationKeys[reader.GetInt32(4)],
            Format = ParseEnum<CompetitionFormat>(reader.GetString(5)),
            Scope = ParseEnum<CompetitionScope>(reader.GetString(6)),
            Reputation = reader.GetInt32(7),
            PointsWin = reader.GetInt32(8),
            PointsDraw = reader.GetInt32(9),
        });

    private List<ContentCoach> ReadCoaches(List<ContentTeam> teams, Dictionary<int, string> nationKeys)
    {
        var teamKeys = teams.ToDictionary(t => t.Id, t => t.Key);
        return QueryAll(
            "SELECT Id, Key, FirstName, LastName, TeamId, NationId, Role, DateOfBirth, Coaching, "
            + "TacticalKnowledge, ManManagement, Fitness, Scouting, PreferredMentality "
            + "FROM Coaches ORDER BY Id;",
            reader => new ContentCoach
            {
                Id = reader.GetInt32(0),
                Key = reader.GetString(1),
                FirstName = reader.GetString(2),
                LastName = reader.GetString(3),
                TeamKey = reader.IsDBNull(4) ? null : teamKeys[reader.GetInt32(4)],
                NationKey = reader.IsDBNull(5) ? null : nationKeys[reader.GetInt32(5)],
                Role = ParseEnum<CoachRole>(reader.GetString(6)),
                DateOfBirth = reader.IsDBNull(7) ? null : SqliteValue.ToDate(reader.GetString(7)),
                Coaching = reader.GetInt32(8),
                TacticalKnowledge = reader.GetInt32(9),
                ManManagement = reader.GetInt32(10),
                Fitness = reader.GetInt32(11),
                Scouting = reader.GetInt32(12),
                PreferredMentality = reader.GetString(13),
            });
    }

    private List<ContentContract> ReadContracts(
        List<ContentPlayer> players, List<ContentCoach> coaches, List<ContentTeam> teams)
    {
        var playerKeys = players.ToDictionary(p => p.Id, p => p.Key);
        var coachKeys = coaches.ToDictionary(c => c.Id, c => c.Key);
        var teamKeys = teams.ToDictionary(t => t.Id, t => t.Key);

        return QueryAll(
            "SELECT Id, Key, PlayerId, CoachId, TeamId, StartDate, EndDate, WeeklyWage, "
            + "SigningBonus, ReleaseClause, SquadStatus FROM Contracts ORDER BY Id;",
            reader => new ContentContract
            {
                Id = reader.GetInt32(0),
                Key = reader.GetString(1),
                PlayerKey = reader.IsDBNull(2) ? null : playerKeys[reader.GetInt32(2)],
                CoachKey = reader.IsDBNull(3) ? null : coachKeys[reader.GetInt32(3)],
                TeamKey = teamKeys[reader.GetInt32(4)],
                StartDate = SqliteValue.ToDate(reader.GetString(5)),
                EndDate = SqliteValue.ToDate(reader.GetString(6)),
                WeeklyWage = reader.GetInt64(7),
                SigningBonus = reader.GetInt64(8),
                ReleaseClause = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                SquadStatus = ParseEnum<SquadStatus>(reader.GetString(10)),
            });
    }

    /// <summary>
    /// Parses the snake_case/lowercase forms the CHECK constraints store back into the enum.
    /// Underscores are stripped rather than mapped, so <c>goalkeeping_coach</c> matches
    /// <c>GoalkeepingCoach</c> under a case-insensitive parse.
    /// </summary>
    private static T ParseEnum<T>(string stored) where T : struct, Enum
        => Enum.Parse<T>(stored.Replace("_", string.Empty), ignoreCase: true);

    private ContentManifest ReadManifest()
    {
        ContentBuildRecord? build = ContentBuildInfo.Read(_connection);
        if (build is null)
            return new ContentManifest();

        return new ContentManifest
        {
            FormatVersion = build.FormatVersion,
            ContentVersion = build.ContentVersion,
            BuildId = build.BuildId,
            Generator = build.Generator,
            ContentHash = build.ContentHash,
        };
    }

    private List<ContentTrait> ReadTraits() => QueryAll(
        "SELECT Id, Key, DisplayName, Aggression, Selfishness, EventWeightBias "
        + "FROM PlayerTraits ORDER BY Id;",
        reader => new ContentTrait
        {
            Id = reader.GetInt32(0),
            Key = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Aggression = reader.GetInt32(3),
            Selfishness = reader.GetInt32(4),
            EventWeightBias = reader.GetInt32(5),
        });

    private List<ContentLeague> ReadLeagues(
        Dictionary<int, string> competitionKeys, Dictionary<int, string> nationKeys) => QueryAll(
        "SELECT Id, Key, Name, Country, Tier, CompetitionId, NationId, PyramidLevel, "
        + "PromotionSlots, RelegationSlots FROM Leagues WHERE Key IS NOT NULL ORDER BY Id;",
        reader => new ContentLeague
        {
            Id = reader.GetInt32(0),
            Key = reader.GetString(1),
            Name = reader.GetString(2),
            Country = reader.GetString(3),
            Tier = (SimulationTier)reader.GetInt32(4),
            CompetitionKey = reader.IsDBNull(5) ? null : competitionKeys[reader.GetInt32(5)],
            NationKey = reader.IsDBNull(6) ? null : nationKeys[reader.GetInt32(6)],
            PyramidLevel = reader.GetInt32(7),
            PromotionSlots = reader.GetInt32(8),
            RelegationSlots = reader.GetInt32(9),
        });

    private List<ContentTeam> ReadTeams(
        List<ContentLeague> leagues, Dictionary<int, string> nationKeys, Dictionary<int, string> stadiumKeys)
    {
        var leagueKeys = leagues.ToDictionary(l => l.Id, l => l.Key);
        return QueryAll(
            "SELECT Id, Key, Name, LeagueId, Budget, EloRating, ShortName, NationId, StadiumId, "
            + "FoundedYear, Reputation FROM Teams WHERE Key IS NOT NULL ORDER BY Id;",
            reader => new ContentTeam
            {
                Id = reader.GetInt32(0),
                Key = reader.GetString(1),
                Name = reader.GetString(2),
                LeagueKey = leagueKeys[reader.GetInt32(3)],
                Budget = reader.GetInt64(4),
                EloRating = reader.GetInt32(5),
                ShortName = reader.IsDBNull(6) ? null : reader.GetString(6),
                NationKey = reader.IsDBNull(7) ? null : nationKeys[reader.GetInt32(7)],
                StadiumKey = reader.IsDBNull(8) ? null : stadiumKeys[reader.GetInt32(8)],
                FoundedYear = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                Reputation = reader.GetInt32(10),
            });
    }

    private List<ContentPlayer> ReadPlayers(
        List<ContentTeam> teams, List<ContentTrait> traits, Dictionary<int, string> nationKeys)
    {
        var teamKeys = teams.ToDictionary(t => t.Id, t => t.Key);
        var traitKeys = traits.ToDictionary(t => t.Id, t => t.Key);
        var assignments = ReadTraitAssignments(traitKeys);

        return QueryAll(
            "SELECT Id, Key, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, "
            + "Shooting, Tackling, Vision, DateOfBirth, NationId, PreferredFoot, PrimaryRole, "
            + "Flank, SquadNumber, HeightCm FROM Players WHERE Key IS NOT NULL ORDER BY Id;",
            reader => new ContentPlayer
            {
                Id = reader.GetInt32(0),
                Key = reader.GetString(1),
                FirstName = reader.GetString(2),
                LastName = reader.GetString(3),
                TeamKey = reader.IsDBNull(4) ? null : teamKeys[reader.GetInt32(4)],
                Attributes = new ContentAttributes
                {
                    Pace = reader.GetInt32(5),
                    Stamina = reader.GetInt32(6),
                    Strength = reader.GetInt32(7),
                    Passing = reader.GetInt32(8),
                    Shooting = reader.GetInt32(9),
                    Tackling = reader.GetInt32(10),
                    Vision = reader.GetInt32(11),
                },
                TraitKeys = assignments.GetValueOrDefault(reader.GetInt32(0), []),
                DateOfBirth = reader.IsDBNull(12) ? null : SqliteValue.ToDate(reader.GetString(12)),
                NationKey = reader.IsDBNull(13) ? null : nationKeys[reader.GetInt32(13)],
                PreferredFoot = ParseEnum<PreferredFoot>(reader.GetString(14)),
                PrimaryRole = ParseEnum<SoccerSim.Core.Tactics.PlayerRole>(reader.GetString(15)),
                Flank = ParseEnum<Flank>(reader.GetString(16)),
                SquadNumber = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                HeightCm = reader.IsDBNull(18) ? null : reader.GetInt32(18),
            });
    }

    private Dictionary<int, List<string>> ReadTraitAssignments(Dictionary<int, string> traitKeys)
    {
        var assignments = new Dictionary<int, List<string>>();
        foreach ((int playerId, int traitId) in QueryAll(
            "SELECT PlayerId, TraitId FROM PlayerTraitAssignments ORDER BY PlayerId, TraitId;",
            reader => (reader.GetInt32(0), reader.GetInt32(1))))
        {
            if (!traitKeys.TryGetValue(traitId, out string? traitKey))
                continue;   // a trait the game generated at runtime, not authored content

            if (!assignments.TryGetValue(playerId, out List<string>? keys))
                assignments[playerId] = keys = [];
            keys.Add(traitKey);
        }

        return assignments;
    }

    private List<ContentHousingItem> ReadHousingItems() => QueryAll(
        "SELECT Id, Key, Name, Cost, StatKey, YieldMultiplier FROM HousingItems ORDER BY Id;",
        reader => new ContentHousingItem
        {
            Id = reader.GetInt32(0),
            Key = reader.GetString(1),
            Name = reader.GetString(2),
            Cost = reader.GetInt64(3),
            StatKey = reader.GetString(4),
            YieldMultiplier = reader.GetDouble(5),
        });

    private ContentWorld ReadWorld(
        List<ContentLeague> leagues, List<ContentTeam> teams, List<ContentPlayer> players)
    {
        var leagueKeys = leagues.ToDictionary(l => l.Id, l => l.Key);
        var teamKeys = teams.ToDictionary(t => t.Id, t => t.Key);
        var playerKeys = players.ToDictionary(p => p.Id, p => p.Key);

        var currentSeasonIds = QueryAll(
            "SELECT CurrentSeasonId FROM Leagues WHERE CurrentSeasonId IS NOT NULL;",
            reader => reader.GetInt32(0)).ToHashSet();

        List<ContentSeason> seasons = QueryAll(
            "SELECT Id, Key, LeagueId, StartDate, EndDate FROM Seasons WHERE Key IS NOT NULL ORDER BY Id;",
            reader => new ContentSeason
            {
                Id = reader.GetInt32(0),
                Key = reader.GetString(1),
                LeagueKey = leagueKeys[reader.GetInt32(2)],
                StartDate = SqliteValue.ToDate(reader.GetString(3)),
                EndDate = SqliteValue.ToDate(reader.GetString(4)),
                IsCurrent = currentSeasonIds.Contains(reader.GetInt32(0)),
            });

        var seasonKeys = seasons.ToDictionary(s => s.Id, s => s.Key);

        List<ContentFixture> fixtures = QueryAll(
            "SELECT Id, Key, SeasonId, HomeTeamId, AwayTeamId, KickoffDate FROM Matches "
            + "WHERE Key IS NOT NULL ORDER BY Id;",
            reader => new ContentFixture
            {
                Id = reader.GetInt32(0),
                Key = reader.GetString(1),
                SeasonKey = seasonKeys[reader.GetInt32(2)],
                HomeTeamKey = teamKeys[reader.GetInt32(3)],
                AwayTeamKey = teamKeys[reader.GetInt32(4)],
                KickoffDate = SqliteValue.ToDate(reader.GetString(5)),
            });

        List<ContentPlayerFinance> finances = QueryAll(
            "SELECT PlayerId, Balance, BaseSalaryWeekly FROM PlayerFinances ORDER BY PlayerId;",
            reader => new
            {
                PlayerId = reader.GetInt32(0),
                Balance = reader.GetInt64(1),
                Salary = reader.GetInt64(2),
            })
            .Where(row => playerKeys.ContainsKey(row.PlayerId))
            .Select(row => new ContentPlayerFinance
            {
                PlayerKey = playerKeys[row.PlayerId],
                Balance = row.Balance,
                BaseSalaryWeekly = row.Salary,
            })
            .ToList();

        string? humanKey = QueryAll("SELECT HumanPlayerId FROM Career WHERE Id = 1;", reader => reader.GetInt32(0))
            .Select(id => playerKeys.GetValueOrDefault(id))
            .FirstOrDefault();

        DateTime startDate = seasons.Count > 0
            ? seasons.Min(s => s.StartDate)
            : new DateTime(2026, 8, 1);

        return new ContentWorld
        {
            StartDate = startDate,
            Seasons = seasons,
            Fixtures = fixtures,
            Finances = finances,
            HumanPlayerKey = humanKey,
        };
    }

    /// <summary>
    /// Runs a query and materialises every row through <paramref name="map"/>. Materialising
    /// rather than handing back a live reader keeps the command's lifetime here, where it can
    /// actually be disposed, and lets a mapper issue its own queries without tripping over an
    /// open reader on the same connection.
    /// </summary>
    private List<T> QueryAll<T>(string sql, Func<SqliteDataReader, T> map)
    {
        var results = new List<T>();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(map(reader));
        return results;
    }
}
