using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class PlayerTraitRepository : SqliteRepositoryBase, IPlayerTraitRepository
{
    private const string SelectColumns = "Id, Key, DisplayName, Aggression, Selfishness, EventWeightBias";

    public PlayerTraitRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<PlayerTrait?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM PlayerTraits WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        return Task.FromResult(ReadSingle(command));
    }

    public Task<PlayerTrait?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM PlayerTraits WHERE Key = $key;");
        command.Parameters.AddWithValue("$key", key);
        return Task.FromResult(ReadSingle(command));
    }

    public Task<IReadOnlyList<PlayerTrait>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var traits = new List<PlayerTrait>();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM PlayerTraits ORDER BY Id;");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            traits.Add(Map(reader));
        return Task.FromResult<IReadOnlyList<PlayerTrait>>(traits);
    }

    public Task<int> AddAsync(PlayerTrait entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"INSERT INTO PlayerTraits (Key, DisplayName, Aggression, Selfishness, EventWeightBias)
              VALUES ($key, $name, $agg, $self, $bias);
              SELECT last_insert_rowid();");
        Bind(command, entity);
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task UpdateAsync(PlayerTrait entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"UPDATE PlayerTraits SET Key = $key, DisplayName = $name, Aggression = $agg,
                Selfishness = $self, EventWeightBias = $bias WHERE Id = $id;");
        Bind(command, entity);
        command.Parameters.AddWithValue("$id", entity.Id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM PlayerTraits WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private static PlayerTrait? ReadSingle(SqliteCommand command)
    {
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static void Bind(SqliteCommand command, PlayerTrait entity)
    {
        command.Parameters.AddWithValue("$key", entity.Key);
        command.Parameters.AddWithValue("$name", entity.DisplayName);
        command.Parameters.AddWithValue("$agg", entity.Aggression);
        command.Parameters.AddWithValue("$self", entity.Selfishness);
        command.Parameters.AddWithValue("$bias", entity.EventWeightBias);
    }

    private static PlayerTrait Map(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt32(3),
        reader.GetInt32(4),
        reader.GetInt32(5));
}
