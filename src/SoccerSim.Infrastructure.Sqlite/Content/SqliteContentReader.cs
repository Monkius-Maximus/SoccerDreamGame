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
        List<ContentTrait> traits = ReadTraits();
        List<ContentLeague> leagues = ReadLeagues();
        List<ContentTeam> teams = ReadTeams(leagues);
        List<ContentPlayer> players = ReadPlayers(teams, traits);

        return new ContentBundle
        {
            Manifest = ReadManifest(),
            Traits = traits,
            Leagues = leagues,
            Teams = teams,
            Players = players,
            HousingItems = ReadHousingItems(),
            World = ReadWorld(leagues, teams, players),
        };
    }

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

    private List<ContentLeague> ReadLeagues() => QueryAll(
        "SELECT Id, Key, Name, Country, Tier FROM Leagues WHERE Key IS NOT NULL ORDER BY Id;",
        reader => new ContentLeague
        {
            Id = reader.GetInt32(0),
            Key = reader.GetString(1),
            Name = reader.GetString(2),
            Country = reader.GetString(3),
            Tier = (SimulationTier)reader.GetInt32(4),
        });

    private List<ContentTeam> ReadTeams(List<ContentLeague> leagues)
    {
        var leagueKeys = leagues.ToDictionary(l => l.Id, l => l.Key);
        return QueryAll(
            "SELECT Id, Key, Name, LeagueId, Budget, EloRating FROM Teams WHERE Key IS NOT NULL ORDER BY Id;",
            reader => new ContentTeam
            {
                Id = reader.GetInt32(0),
                Key = reader.GetString(1),
                Name = reader.GetString(2),
                LeagueKey = leagueKeys[reader.GetInt32(3)],
                Budget = reader.GetInt64(4),
                EloRating = reader.GetInt32(5),
            });
    }

    private List<ContentPlayer> ReadPlayers(List<ContentTeam> teams, List<ContentTrait> traits)
    {
        var teamKeys = teams.ToDictionary(t => t.Id, t => t.Key);
        var traitKeys = traits.ToDictionary(t => t.Id, t => t.Key);
        var assignments = ReadTraitAssignments(traitKeys);

        return QueryAll(
            "SELECT Id, Key, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, "
            + "Shooting, Tackling, Vision FROM Players WHERE Key IS NOT NULL ORDER BY Id;",
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
