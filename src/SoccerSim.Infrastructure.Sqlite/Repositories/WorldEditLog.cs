using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class WorldEditLog : SqliteRepositoryBase, IWorldEditLog
{
    public WorldEditLog(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task RecordAsync(WorldEdit edit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand(
            @"INSERT INTO WorldEdits
                  (EntityType, EntityId, FieldPath, OldValue, NewValue, EditedAt, HistoryId)
              VALUES ($type, $id, $path, $old, $new, $at, $history);");
        command.Parameters.AddWithValue("$type", edit.EntityType.ToString());
        command.Parameters.AddWithValue("$id", edit.EntityId);
        command.Parameters.AddWithValue("$path", edit.FieldPath);
        command.Parameters.AddWithValue("$old", WorldRow.OrNull(edit.OldValue));
        command.Parameters.AddWithValue("$new", WorldRow.OrNull(edit.NewValue));
        command.Parameters.AddWithValue("$at", SqliteValue.ToText(edit.EditedAt));
        command.Parameters.AddWithValue("$history", (object?)edit.HistoryId ?? DBNull.Value);
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand("SELECT COUNT(*) FROM WorldEdits WHERE ExportedAt IS NULL;");
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand("SELECT COUNT(*) FROM WorldEdits;");
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task<IReadOnlyDictionary<long, int>> CountByActAsync(
        IReadOnlyList<long> actIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var counts = new Dictionary<long, int>();
        if (actIds.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<long, int>>(counts);

        // One parameter per id rather than a joined string: ids are numbers here and always will
        // be, but a query built by concatenation is a habit worth not having.
        string placeholders = string.Join(", ", actIds.Select((_, index) => $"$a{index}"));

        using SqliteCommand command = CreateCommand(
            $@"SELECT HistoryId, COUNT(*) FROM WorldEdits
               WHERE HistoryId IN ({placeholders}) GROUP BY HistoryId;");

        for (int index = 0; index < actIds.Count; index++)
            command.Parameters.AddWithValue($"$a{index}", actIds[index]);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            counts[reader.GetInt64(0)] = reader.GetInt32(1);

        return Task.FromResult<IReadOnlyDictionary<long, int>>(counts);
    }

    public Task<IReadOnlyList<WorldEditEntry>> ListAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = new List<WorldEditEntry>();
        using SqliteCommand command = CreateCommand(
            @"SELECT Id, EntityType, EntityId, FieldPath, OldValue, NewValue, EditedAt,
                     HistoryId, ExportedAt
              FROM WorldEdits ORDER BY Id DESC LIMIT $limit OFFSET $offset;");
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new WorldEditEntry(
                reader.GetInt64(0),
                new WorldEdit(
                    WorldRow.Enum<WorldEntityType>(reader, 1),
                    reader.GetString(2),
                    reader.GetString(3),
                    WorldRow.NullableString(reader, 4),
                    WorldRow.NullableString(reader, 5),
                    SqliteValue.ToDate(reader.GetString(6)),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7)),
                WorldRow.NullableString(reader, 8) is { } exported
                    ? SqliteValue.ToDate(exported)
                    : null));
        }

        return Task.FromResult<IReadOnlyList<WorldEditEntry>>(entries);
    }

    public Task<int> MarkExportedAsync(DateTime exportedAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand(
            "UPDATE WorldEdits SET ExportedAt = $at WHERE ExportedAt IS NULL;");
        command.Parameters.AddWithValue("$at", SqliteValue.ToText(exportedAt));
        return Task.FromResult(command.ExecuteNonQuery());
    }
}
