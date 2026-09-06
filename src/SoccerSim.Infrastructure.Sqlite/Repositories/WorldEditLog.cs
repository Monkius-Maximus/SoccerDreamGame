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
            @"INSERT INTO WorldEdits (EntityType, EntityId, FieldPath, OldValue, NewValue, EditedAt)
              VALUES ($type, $id, $path, $old, $new, $at);");
        command.Parameters.AddWithValue("$type", edit.EntityType.ToString());
        command.Parameters.AddWithValue("$id", edit.EntityId);
        command.Parameters.AddWithValue("$path", edit.FieldPath);
        command.Parameters.AddWithValue("$old", WorldRow.OrNull(edit.OldValue));
        command.Parameters.AddWithValue("$new", WorldRow.OrNull(edit.NewValue));
        command.Parameters.AddWithValue("$at", SqliteValue.ToText(edit.EditedAt));
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand("SELECT COUNT(*) FROM WorldEdits WHERE ExportedAt IS NULL;");
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task<IReadOnlyList<WorldEdit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var edits = new List<WorldEdit>();
        using SqliteCommand command = CreateCommand(
            @"SELECT EntityType, EntityId, FieldPath, OldValue, NewValue, EditedAt
              FROM WorldEdits ORDER BY Id DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            edits.Add(new WorldEdit(
                WorldRow.Enum<WorldEntityType>(reader, 0),
                reader.GetString(1),
                reader.GetString(2),
                WorldRow.NullableString(reader, 3),
                WorldRow.NullableString(reader, 4),
                SqliteValue.ToDate(reader.GetString(5))));
        }

        return Task.FromResult<IReadOnlyList<WorldEdit>>(edits);
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
