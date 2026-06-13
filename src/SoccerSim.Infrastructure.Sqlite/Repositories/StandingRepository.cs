using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class StandingRepository : SqliteRepositoryBase, IStandingRepository
{
    private const string SelectColumns =
        "SeasonId, TeamId, Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points";

    public StandingRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<Standing?> GetAsync(int seasonId, int teamId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            $"SELECT {SelectColumns} FROM Standings WHERE SeasonId = $sid AND TeamId = $tid;");
        command.Parameters.AddWithValue("$sid", seasonId);
        command.Parameters.AddWithValue("$tid", teamId);
        using SqliteDataReader reader = command.ExecuteReader();
        Standing? standing = reader.Read() ? Map(reader) : null;
        return Task.FromResult(standing);
    }

    public Task<IReadOnlyList<Standing>> GetTableAsync(int seasonId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var standings = new List<Standing>();
        using SqliteCommand command = CreateCommand(
            $@"SELECT {SelectColumns} FROM Standings WHERE SeasonId = $sid
               ORDER BY Points DESC, (GoalsFor - GoalsAgainst) DESC, GoalsFor DESC;");
        command.Parameters.AddWithValue("$sid", seasonId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            standings.Add(Map(reader));
        return Task.FromResult<IReadOnlyList<Standing>>(standings);
    }

    public Task UpsertAsync(Standing standing, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"INSERT INTO Standings (SeasonId, TeamId, Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points)
              VALUES ($sid, $tid, $p, $w, $d, $l, $gf, $ga, $pts)
              ON CONFLICT (SeasonId, TeamId) DO UPDATE SET
                Played = excluded.Played, Won = excluded.Won, Drawn = excluded.Drawn,
                Lost = excluded.Lost, GoalsFor = excluded.GoalsFor,
                GoalsAgainst = excluded.GoalsAgainst, Points = excluded.Points;");
        command.Parameters.AddWithValue("$sid", standing.SeasonId);
        command.Parameters.AddWithValue("$tid", standing.TeamId);
        command.Parameters.AddWithValue("$p", standing.Played);
        command.Parameters.AddWithValue("$w", standing.Won);
        command.Parameters.AddWithValue("$d", standing.Drawn);
        command.Parameters.AddWithValue("$l", standing.Lost);
        command.Parameters.AddWithValue("$gf", standing.GoalsFor);
        command.Parameters.AddWithValue("$ga", standing.GoalsAgainst);
        command.Parameters.AddWithValue("$pts", standing.Points);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private static Standing Map(SqliteDataReader reader) => new()
    {
        SeasonId = reader.GetInt32(0),
        TeamId = reader.GetInt32(1),
        Played = reader.GetInt32(2),
        Won = reader.GetInt32(3),
        Drawn = reader.GetInt32(4),
        Lost = reader.GetInt32(5),
        GoalsFor = reader.GetInt32(6),
        GoalsAgainst = reader.GetInt32(7),
        Points = reader.GetInt32(8),
    };
}
