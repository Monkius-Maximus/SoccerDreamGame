using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class LeagueRepository : SqliteRepositoryBase, ILeagueRepository
{
    private const string SelectColumns = "Id, Name, Country, Tier, CurrentSeasonId";

    public LeagueRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<League?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Leagues WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        League? league = reader.Read() ? Map(reader) : null;
        return Task.FromResult(league);
    }

    public Task<IReadOnlyList<League>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query($"SELECT {SelectColumns} FROM Leagues ORDER BY Id;", bind: null));
    }

    public Task<IReadOnlyList<League>> ListByTierAsync(SimulationTier tier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(
            $"SELECT {SelectColumns} FROM Leagues WHERE Tier = $tier ORDER BY Id;",
            command => command.Parameters.AddWithValue("$tier", (int)tier)));
    }

    public Task<int> AddAsync(League entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"INSERT INTO Leagues (Name, Country, Tier, CurrentSeasonId)
              VALUES ($name, $country, $tier, $season);
              SELECT last_insert_rowid();");
        Bind(command, entity);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task UpdateAsync(League entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            "UPDATE Leagues SET Name = $name, Country = $country, Tier = $tier, CurrentSeasonId = $season WHERE Id = $id;");
        Bind(command, entity);
        command.Parameters.AddWithValue("$id", entity.Id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Leagues WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<League> Query(string sql, Action<SqliteCommand>? bind)
    {
        var leagues = new List<League>();
        using SqliteCommand command = CreateCommand(sql);
        bind?.Invoke(command);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            leagues.Add(Map(reader));
        return leagues;
    }

    private static void Bind(SqliteCommand command, League entity)
    {
        command.Parameters.AddWithValue("$name", entity.Name);
        command.Parameters.AddWithValue("$country", entity.Country);
        command.Parameters.AddWithValue("$tier", (int)entity.Tier);
        command.Parameters.AddWithValue("$season", (object?)entity.CurrentSeasonId ?? DBNull.Value);
    }

    private static League Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Country = reader.GetString(2),
        Tier = (SimulationTier)reader.GetInt32(3),
        CurrentSeasonId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
    };
}
