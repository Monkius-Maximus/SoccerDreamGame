using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class MatchRepository : SqliteRepositoryBase, IMatchRepository
{
    private const string SelectColumns =
        "Id, SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played, HomeGoals, AwayGoals";

    public MatchRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<Match?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Matches WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        Match? match = reader.Read() ? Map(reader) : null;
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Match>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query($"SELECT {SelectColumns} FROM Matches ORDER BY KickoffDate;", bind: null));
    }

    public Task<IReadOnlyList<Match>> ListFixturesAsync(int leagueId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(
            $@"SELECT {SelectColumns} FROM Matches
               WHERE LeagueId = $lid AND KickoffDate >= $from AND KickoffDate < $to
               ORDER BY KickoffDate;",
            command =>
            {
                command.Parameters.AddWithValue("$lid", leagueId);
                command.Parameters.AddWithValue("$from", SqliteValue.ToText(from));
                command.Parameters.AddWithValue("$to", SqliteValue.ToText(to));
            }));
    }

    public Task<int> AddAsync(Match entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"INSERT INTO Matches (SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played, HomeGoals, AwayGoals)
              VALUES ($sid, $lid, $home, $away, $kickoff, $played, $hg, $ag);
              SELECT last_insert_rowid();");
        Bind(command, entity);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task UpdateAsync(Match entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"UPDATE Matches SET SeasonId = $sid, LeagueId = $lid, HomeTeamId = $home, AwayTeamId = $away,
                KickoffDate = $kickoff, Played = $played, HomeGoals = $hg, AwayGoals = $ag WHERE Id = $id;");
        Bind(command, entity);
        command.Parameters.AddWithValue("$id", entity.Id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Matches WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task BulkInsertResultsAsync(IEnumerable<MatchResult> results, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (MatchResult result in results)
        {
            using (SqliteCommand update = CreateCommand(
                "UPDATE Matches SET Played = 1, HomeGoals = $hg, AwayGoals = $ag WHERE Id = $id;"))
            {
                update.Parameters.AddWithValue("$hg", result.HomeGoals);
                update.Parameters.AddWithValue("$ag", result.AwayGoals);
                update.Parameters.AddWithValue("$id", result.MatchId);
                update.ExecuteNonQuery();
            }

            foreach (ScorerLine scorer in result.Scorers)
            {
                using SqliteCommand goal = CreateCommand(
                    "INSERT INTO Goals (MatchId, PlayerId, Minute) VALUES ($mid, $pid, $minute);");
                goal.Parameters.AddWithValue("$mid", result.MatchId);
                goal.Parameters.AddWithValue("$pid", scorer.PlayerId);
                goal.Parameters.AddWithValue("$minute", scorer.Minute);
                goal.ExecuteNonQuery();
            }
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<Match> Query(string sql, Action<SqliteCommand>? bind)
    {
        var matches = new List<Match>();
        using SqliteCommand command = CreateCommand(sql);
        bind?.Invoke(command);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            matches.Add(Map(reader));
        return matches;
    }

    private static void Bind(SqliteCommand command, Match entity)
    {
        command.Parameters.AddWithValue("$sid", entity.SeasonId);
        command.Parameters.AddWithValue("$lid", entity.LeagueId);
        command.Parameters.AddWithValue("$home", entity.HomeTeamId);
        command.Parameters.AddWithValue("$away", entity.AwayTeamId);
        command.Parameters.AddWithValue("$kickoff", SqliteValue.ToText(entity.KickoffDate));
        command.Parameters.AddWithValue("$played", entity.Played ? 1 : 0);
        command.Parameters.AddWithValue("$hg", (object?)entity.HomeGoals ?? DBNull.Value);
        command.Parameters.AddWithValue("$ag", (object?)entity.AwayGoals ?? DBNull.Value);
    }

    private static Match Map(SqliteDataReader reader) => new()
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
