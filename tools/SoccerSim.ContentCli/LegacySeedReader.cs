using Microsoft.Data.Sqlite;
using SoccerSim.Content;
using SoccerSim.Content.Model;
using SoccerSim.Core.Simulation;

namespace SoccerSim.ContentCli;

/// <summary>
/// One-shot port of the hand-written <c>sql/9999_seed_dev.sql</c> world into a content bundle.
///
/// Existing integer ids are carried across verbatim so nothing renumbers — the save schema, the
/// LOD routing and the tests all keep pointing at the same rows. Keys are derived from the names
/// that were already there, so no content is transcribed by hand (which is exactly where a
/// migration like this normally introduces typos).
///
/// This class exists to be run once and then deleted along with the seed file it reads.
/// </summary>
internal static class LegacySeedReader
{
    public static ContentBundle Read(SqliteConnection connection)
    {
        List<ContentTrait> traits = ReadTraits(connection);
        List<ContentLeague> leagues = ReadLeagues(connection);
        List<ContentTeam> teams = ReadTeams(connection, leagues);
        List<ContentPlayer> players = ReadPlayers(connection, teams, traits);
        List<ContentHousingItem> housing = ReadHousingItems(connection);
        ContentWorld world = ReadWorld(connection, leagues, teams, players);

        return new ContentBundle
        {
            Traits = traits,
            Leagues = leagues,
            Teams = teams,
            Players = players,
            HousingItems = housing,
            World = world,
        };
    }

    private static List<ContentTrait> ReadTraits(SqliteConnection connection) => Query(
        connection,
        "SELECT Id, Key, DisplayName, Aggression, Selfishness, EventWeightBias FROM PlayerTraits ORDER BY Id;",
        reader => new ContentTrait
        {
            Id = reader.GetInt32(0),
            // PlayerTraits already carried a Key ('hot_headed'), and game code looks traits up
            // by it. Preserve it exactly rather than re-slugifying the display name.
            Key = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Aggression = reader.GetInt32(3),
            Selfishness = reader.GetInt32(4),
            EventWeightBias = reader.GetInt32(5),
        });

    private static List<ContentLeague> ReadLeagues(SqliteConnection connection) => Query(
        connection,
        "SELECT Id, Name, Country, Tier FROM Leagues ORDER BY Id;",
        reader => new ContentLeague
        {
            Id = reader.GetInt32(0),
            Key = ContentKey.Slugify(reader.GetString(1)),
            Name = reader.GetString(1),
            Country = reader.GetString(2),
            Tier = (SimulationTier)reader.GetInt32(3),
        });

    private static List<ContentTeam> ReadTeams(SqliteConnection connection, List<ContentLeague> leagues)
    {
        var leagueKeys = leagues.ToDictionary(l => l.Id, l => l.Key);
        return Query(
            connection,
            "SELECT Id, Name, LeagueId, Budget, EloRating FROM Teams ORDER BY Id;",
            reader => new ContentTeam
            {
                Id = reader.GetInt32(0),
                Key = ContentKey.Slugify(reader.GetString(1)),
                Name = reader.GetString(1),
                LeagueKey = leagueKeys[reader.GetInt32(2)],
                Budget = reader.GetInt64(3),
                EloRating = reader.GetInt32(4),
            });
    }

    private static List<ContentPlayer> ReadPlayers(
        SqliteConnection connection, List<ContentTeam> teams, List<ContentTrait> traits)
    {
        var teamKeys = teams.ToDictionary(t => t.Id, t => t.Key);
        var traitKeys = traits.ToDictionary(t => t.Id, t => t.Key);

        var assignments = new Dictionary<int, List<string>>();
        foreach ((int playerId, int traitId) in Query(
            connection,
            "SELECT PlayerId, TraitId FROM PlayerTraitAssignments ORDER BY PlayerId, TraitId;",
            reader => (reader.GetInt32(0), reader.GetInt32(1))))
        {
            if (!assignments.TryGetValue(playerId, out List<string>? keys))
                assignments[playerId] = keys = [];
            keys.Add(traitKeys[traitId]);
        }

        return Query(
            connection,
            "SELECT Id, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, Shooting, "
            + "Tackling, Vision FROM Players ORDER BY Id;",
            reader => new ContentPlayer
            {
                Id = reader.GetInt32(0),
                Key = ContentKey.Slugify(reader.GetString(1) + " " + reader.GetString(2)),
                FirstName = reader.GetString(1),
                LastName = reader.GetString(2),
                TeamKey = reader.IsDBNull(3) ? null : teamKeys[reader.GetInt32(3)],
                Attributes = new ContentAttributes
                {
                    Pace = reader.GetInt32(4),
                    Stamina = reader.GetInt32(5),
                    Strength = reader.GetInt32(6),
                    Passing = reader.GetInt32(7),
                    Shooting = reader.GetInt32(8),
                    Tackling = reader.GetInt32(9),
                    Vision = reader.GetInt32(10),
                },
                TraitKeys = assignments.GetValueOrDefault(reader.GetInt32(0), []),
            });
    }

    private static List<ContentHousingItem> ReadHousingItems(SqliteConnection connection) => Query(
        connection,
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

    private static ContentWorld ReadWorld(
        SqliteConnection connection,
        List<ContentLeague> leagues,
        List<ContentTeam> teams,
        List<ContentPlayer> players)
    {
        var leagueKeys = leagues.ToDictionary(l => l.Id, l => l.Key);
        var teamKeys = teams.ToDictionary(t => t.Id, t => t.Key);
        var playerKeys = players.ToDictionary(p => p.Id, p => p.Key);

        var currentSeasonIds = Query(
            connection,
            "SELECT CurrentSeasonId FROM Leagues WHERE CurrentSeasonId IS NOT NULL;",
            reader => reader.GetInt32(0)).ToHashSet();

        List<ContentSeason> seasons = Query(
            connection,
            "SELECT Id, LeagueId, StartDate, EndDate FROM Seasons ORDER BY Id;",
            reader =>
            {
                DateTime start = DateTime.Parse(reader.GetString(2));
                DateTime end = DateTime.Parse(reader.GetString(3));
                string leagueKey = leagueKeys[reader.GetInt32(1)];
                return new ContentSeason
                {
                    Id = reader.GetInt32(0),
                    // e.g. "premier-division-2026-27": readable, and unique per league-season.
                    Key = $"{leagueKey}-{start:yyyy}-{end:yy}",
                    LeagueKey = leagueKey,
                    StartDate = start,
                    EndDate = end,
                    IsCurrent = currentSeasonIds.Contains(reader.GetInt32(0)),
                };
            });

        var seasonKeys = seasons.ToDictionary(s => s.Id, s => s.Key);

        List<ContentFixture> fixtures = Query(
            connection,
            "SELECT Id, SeasonId, HomeTeamId, AwayTeamId, KickoffDate FROM Matches ORDER BY Id;",
            reader =>
            {
                string home = teamKeys[reader.GetInt32(2)];
                string away = teamKeys[reader.GetInt32(3)];
                DateTime kickoff = DateTime.Parse(reader.GetString(4));
                return new ContentFixture
                {
                    Id = reader.GetInt32(0),
                    Key = $"{home}-vs-{away}-{kickoff:yyyy-MM-dd}",
                    SeasonKey = seasonKeys[reader.GetInt32(1)],
                    HomeTeamKey = home,
                    AwayTeamKey = away,
                    KickoffDate = kickoff,
                };
            });

        List<ContentPlayerFinance> finances = Query(
            connection,
            "SELECT PlayerId, Balance, BaseSalaryWeekly FROM PlayerFinances ORDER BY PlayerId;",
            reader => new ContentPlayerFinance
            {
                PlayerKey = playerKeys[reader.GetInt32(0)],
                Balance = reader.GetInt64(1),
                BaseSalaryWeekly = reader.GetInt64(2),
            });

        string? humanKey = Query(
            connection,
            "SELECT HumanPlayerId FROM Career WHERE Id = 1;",
            reader => reader.GetInt32(0))
            .Select(id => playerKeys.GetValueOrDefault(id))
            .FirstOrDefault();

        return new ContentWorld
        {
            StartDate = seasons.Count > 0 ? seasons.Min(s => s.StartDate) : new DateTime(2026, 8, 1),
            Seasons = seasons,
            Fixtures = fixtures,
            Finances = finances,
            HumanPlayerKey = humanKey,
        };
    }

    private static List<T> Query<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map)
    {
        var results = new List<T>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(map(reader));
        return results;
    }
}
