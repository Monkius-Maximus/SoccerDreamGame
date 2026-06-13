using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class TeamRepository : SqliteRepositoryBase, ITeamRepository
{
    private const string SelectColumns = "Id, Name, LeagueId, Budget, EloRating";

    public TeamRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<Team?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Teams WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        Team? team = reader.Read() ? Map(reader) : null;
        return Task.FromResult(team);
    }

    public Task<IReadOnlyList<Team>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query($"SELECT {SelectColumns} FROM Teams ORDER BY Id;", bind: null));
    }

    public Task<IReadOnlyList<Team>> ListByLeagueAsync(int leagueId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(
            $"SELECT {SelectColumns} FROM Teams WHERE LeagueId = $lid ORDER BY Id;",
            command => command.Parameters.AddWithValue("$lid", leagueId)));
    }

    public Task<int> AddAsync(Team entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"INSERT INTO Teams (Name, LeagueId, Budget, EloRating)
              VALUES ($name, $lid, $budget, $elo);
              SELECT last_insert_rowid();");
        Bind(command, entity);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task UpdateAsync(Team entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            "UPDATE Teams SET Name = $name, LeagueId = $lid, Budget = $budget, EloRating = $elo WHERE Id = $id;");
        Bind(command, entity);
        command.Parameters.AddWithValue("$id", entity.Id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Teams WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<Team> Query(string sql, Action<SqliteCommand>? bind)
    {
        var teams = new List<Team>();
        using SqliteCommand command = CreateCommand(sql);
        bind?.Invoke(command);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            teams.Add(Map(reader));
        return teams;
    }

    private static void Bind(SqliteCommand command, Team entity)
    {
        command.Parameters.AddWithValue("$name", entity.Name);
        command.Parameters.AddWithValue("$lid", entity.LeagueId);
        command.Parameters.AddWithValue("$budget", entity.Budget);
        command.Parameters.AddWithValue("$elo", entity.EloRating);
    }

    private static Team Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        LeagueId = reader.GetInt32(2),
        Budget = reader.GetInt64(3),
        EloRating = reader.GetInt32(4),
    };
}
