using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IFixtureGateway"/> for the LOD simulation. Every query
/// operates purely on rows — no rendering assets are loaded into RAM (GDD §7) — which
/// is what lets Tier 2/Tier 3 leagues be resolved cheaply in the background.
/// </summary>
public sealed class SqliteFixtureGateway : IFixtureGateway
{
    private readonly SqliteConnection _connection;

    public SqliteFixtureGateway(SqliteConnection connection) => _connection = connection;

    public SimulationTier GetTier(int leagueId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT Tier FROM Leagues WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", leagueId);
        object? value = command.ExecuteScalar();
        return value is null
            ? throw new InvalidOperationException($"League {leagueId} not found.")
            : (SimulationTier)Convert.ToInt32(value);
    }

    public IReadOnlyList<MatchContext> GetDueMatches(DateTime date, SimulationTier tier)
    {
        var matchIds = new List<int>();
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText =
                @"SELECT m.Id
                  FROM Matches m
                  JOIN Leagues l ON l.Id = m.LeagueId
                  WHERE l.Tier = $tier AND m.Played = 0 AND m.KickoffDate <= $date
                  ORDER BY m.KickoffDate;";
            command.Parameters.AddWithValue("$tier", (int)tier);
            command.Parameters.AddWithValue("$date", SqliteValue.ToText(date));
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                matchIds.Add(reader.GetInt32(0));
        }

        // Only Tier 1 runs the attribute-driven minute engine; Tier 2/3 stay Elo-only,
        // so we avoid loading per-player attributes for those cheap background snapshots.
        bool includeAttributes = tier == SimulationTier.ActiveHuman;
        var contexts = new List<MatchContext>(matchIds.Count);
        foreach (int matchId in matchIds)
        {
            MatchContext? context = BuildContext(matchId, includeAttributes);
            if (context is not null)
                contexts.Add(context);
        }

        return contexts;
    }

    // On-demand lookups (e.g. the player's own rendered fixture) always load full
    // per-player attributes so the Tier 1 minute engine runs at full fidelity.
    public MatchContext? GetMatchContext(int matchId) => BuildContext(matchId, includeAttributes: true);

    private MatchContext? BuildContext(int matchId, bool includeAttributes)
    {
        Match? match = LoadMatch(matchId);
        if (match is null)
            return null;

        return new MatchContext(
            match,
            LoadTeamSnapshot(match.HomeTeamId, includeAttributes),
            LoadTeamSnapshot(match.AwayTeamId, includeAttributes));
    }

    public int? GetNextUnplayedMatchId(SimulationTier tier, int? teamId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            @"SELECT m.Id
              FROM Matches m
              JOIN Leagues l ON l.Id = m.LeagueId
              WHERE l.Tier = $tier AND m.Played = 0
                AND ($team IS NULL OR m.HomeTeamId = $team OR m.AwayTeamId = $team)
              ORDER BY m.KickoffDate
              LIMIT 1;";
        command.Parameters.AddWithValue("$tier", (int)tier);
        command.Parameters.AddWithValue("$team", (object?)teamId ?? DBNull.Value);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    public MatchDisplayInfo? GetMatchDisplayInfo(int matchId)
    {
        Match? match = LoadMatch(matchId);
        if (match is null)
            return null;

        var playerNames = new Dictionary<int, string>();
        LoadPlayerNames(match.HomeTeamId, playerNames);
        LoadPlayerNames(match.AwayTeamId, playerNames);

        return new MatchDisplayInfo(
            match.HomeTeamId,
            LoadTeamName(match.HomeTeamId),
            match.AwayTeamId,
            LoadTeamName(match.AwayTeamId),
            playerNames);
    }

    public void SaveResult(MatchContext context, MatchResult result)
    {
        using (SqliteCommand update = _connection.CreateCommand())
        {
            update.CommandText = "UPDATE Matches SET Played = 1, HomeGoals = $hg, AwayGoals = $ag WHERE Id = $id;";
            update.Parameters.AddWithValue("$hg", result.HomeGoals);
            update.Parameters.AddWithValue("$ag", result.AwayGoals);
            update.Parameters.AddWithValue("$id", result.MatchId);
            update.ExecuteNonQuery();
        }

        foreach (ScorerLine scorer in result.Scorers)
        {
            using SqliteCommand goal = _connection.CreateCommand();
            goal.CommandText = "INSERT INTO Goals (MatchId, PlayerId, Minute) VALUES ($mid, $pid, $minute);";
            goal.Parameters.AddWithValue("$mid", result.MatchId);
            goal.Parameters.AddWithValue("$pid", scorer.PlayerId);
            goal.Parameters.AddWithValue("$minute", scorer.Minute);
            goal.ExecuteNonQuery();
        }

        // Per-player ratings feed Tier 1/2 form recalculation (Tier 3 emits none).
        foreach ((int playerId, double rating) in result.PlayerRatings)
        {
            using SqliteCommand rate = _connection.CreateCommand();
            rate.CommandText =
                @"INSERT INTO PlayerMatchRatings (MatchId, PlayerId, Rating) VALUES ($mid, $pid, $rating)
                  ON CONFLICT (MatchId, PlayerId) DO UPDATE SET Rating = excluded.Rating;";
            rate.Parameters.AddWithValue("$mid", result.MatchId);
            rate.Parameters.AddWithValue("$pid", playerId);
            rate.Parameters.AddWithValue("$rating", rating);
            rate.ExecuteNonQuery();
        }

        UpdateStanding(context.Match.SeasonId, context.Home.TeamId, result.HomeGoals, result.AwayGoals);
        UpdateStanding(context.Match.SeasonId, context.Away.TeamId, result.AwayGoals, result.HomeGoals);
    }

    private Match? LoadMatch(int matchId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            @"SELECT Id, SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played, HomeGoals, AwayGoals
              FROM Matches WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", matchId);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new Match
        {
            Id = reader.GetInt32(0),
            SeasonId = reader.GetInt32(1),
            LeagueId = reader.GetInt32(2),
            HomeTeamId = reader.GetInt32(3),
            AwayTeamId = reader.GetInt32(4),
            KickoffDate = SqliteValue.ToDate(reader.GetString(5)),
            Played = reader.GetInt32(6) == 1,
            HomeGoals = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            AwayGoals = reader.IsDBNull(8) ? null : reader.GetInt32(8),
        };
    }

    private TeamSnapshot LoadTeamSnapshot(int teamId, bool includeAttributes)
    {
        int elo = LoadElo(teamId);

        var squad = new List<int>();
        List<PlayerSnapshot>? players = includeAttributes ? new List<PlayerSnapshot>() : null;

        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = includeAttributes
                ? "SELECT Id, Pace, Stamina, Strength, Passing, Shooting, Tackling, Vision FROM Players WHERE TeamId = $tid ORDER BY Id;"
                : "SELECT Id FROM Players WHERE TeamId = $tid ORDER BY Id;";
            command.Parameters.AddWithValue("$tid", teamId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int playerId = reader.GetInt32(0);
                squad.Add(playerId);
                players?.Add(new PlayerSnapshot(playerId, new PlayerAttributes(
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7))));
            }
        }

        return players is null
            ? new TeamSnapshot(teamId, elo, squad)
            : new TeamSnapshot(teamId, elo, squad) { Players = players };
    }

    private int LoadElo(int teamId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT EloRating FROM Teams WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", teamId);
        object? value = command.ExecuteScalar();
        return value is null ? 1500 : Convert.ToInt32(value);
    }

    private string LoadTeamName(int teamId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Teams WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", teamId);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? $"Team {teamId}" : (string)value;
    }

    private void LoadPlayerNames(int teamId, Dictionary<int, string> into)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, FirstName, LastName FROM Players WHERE TeamId = $tid ORDER BY Id;";
        command.Parameters.AddWithValue("$tid", teamId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            into[reader.GetInt32(0)] = $"{reader.GetString(1)} {reader.GetString(2)}";
    }

    private void UpdateStanding(int seasonId, int teamId, int goalsFor, int goalsAgainst)
    {
        int won = goalsFor > goalsAgainst ? 1 : 0;
        int drawn = goalsFor == goalsAgainst ? 1 : 0;
        int lost = goalsFor < goalsAgainst ? 1 : 0;
        int points = (won * 3) + drawn;

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO Standings (SeasonId, TeamId, Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points)
              VALUES ($sid, $tid, 1, $w, $d, $l, $gf, $ga, $pts)
              ON CONFLICT (SeasonId, TeamId) DO UPDATE SET
                Played = Played + 1,
                Won = Won + excluded.Won,
                Drawn = Drawn + excluded.Drawn,
                Lost = Lost + excluded.Lost,
                GoalsFor = GoalsFor + excluded.GoalsFor,
                GoalsAgainst = GoalsAgainst + excluded.GoalsAgainst,
                Points = Points + excluded.Points;";
        command.Parameters.AddWithValue("$sid", seasonId);
        command.Parameters.AddWithValue("$tid", teamId);
        command.Parameters.AddWithValue("$w", won);
        command.Parameters.AddWithValue("$d", drawn);
        command.Parameters.AddWithValue("$l", lost);
        command.Parameters.AddWithValue("$gf", goalsFor);
        command.Parameters.AddWithValue("$ga", goalsAgainst);
        command.Parameters.AddWithValue("$pts", points);
        command.ExecuteNonQuery();
    }
}
