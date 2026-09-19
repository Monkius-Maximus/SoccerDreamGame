using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class WorldHistoryRepository : SqliteRepositoryBase, IWorldHistory
{
    private const string SelectColumns = "Id, Label, TakenAt, Document, Scale";

    public WorldHistoryRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<long> PushAsync(
        string label,
        string document,
        string scale,
        int cap,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long id;

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO WorldHistory (Label, TakenAt, Document, Scale)
              VALUES ($label, $at, $document, $scale)
              RETURNING Id;"))
        {
            command.Parameters.AddWithValue("$label", label);
            command.Parameters.AddWithValue("$at", SqliteValue.ToText(DateTime.UtcNow));
            command.Parameters.AddWithValue("$document", document);
            command.Parameters.AddWithValue("$scale", scale);
            id = Convert.ToInt64(command.ExecuteScalar());
        }

        // Drop everything past the cap in the same breath as pushing, so the table cannot grow
        // between a push and a tidy-up that might not happen.
        using (SqliteCommand trim = CreateCommand(
            @"DELETE FROM WorldHistory WHERE Id NOT IN
                  (SELECT Id FROM WorldHistory ORDER BY Id DESC LIMIT $cap);"))
        {
            trim.Parameters.AddWithValue("$cap", cap);
            trim.ExecuteNonQuery();
        }

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<WorldHistoryStep>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var steps = new List<WorldHistoryStep>();

        // Id, Label and TakenAt only. Selecting Document here would hand the screen 17 MB to draw
        // a list of twenty-five lines.
        using SqliteCommand command = CreateCommand(
            "SELECT Id, Label, TakenAt FROM WorldHistory ORDER BY Id DESC;");
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            steps.Add(new WorldHistoryStep(
                reader.GetInt64(0),
                reader.GetString(1),
                SqliteValue.ToDate(reader.GetString(2))));
        }

        return Task.FromResult<IReadOnlyList<WorldHistoryStep>>(steps);
    }

    public Task<WorldHistoryEntry?> TakeUntilAsync(long entryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WorldHistoryEntry? entry;

        using (SqliteCommand read = CreateCommand(
            $"SELECT {SelectColumns} FROM WorldHistory WHERE Id = $id;"))
        {
            read.Parameters.AddWithValue("$id", entryId);
            using SqliteDataReader reader = read.ExecuteReader();
            entry = reader.Read() ? Entry(reader) : null;
        }

        if (entry is null)
            return Task.FromResult<WorldHistoryEntry?>(null);

        // Everything newer goes with it: walking back past an act discards the acts that came
        // after, because the stack runs in one direction and nothing can replay them.
        using (SqliteCommand delete = CreateCommand("DELETE FROM WorldHistory WHERE Id >= $id;"))
        {
            delete.Parameters.AddWithValue("$id", entryId);
            delete.ExecuteNonQuery();
        }

        return Task.FromResult<WorldHistoryEntry?>(entry);
    }

    public Task<WorldHistoryEntry?> PeekAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Newest());
    }

    public Task<WorldHistoryEntry?> PopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WorldHistoryEntry? entry = Newest();
        if (entry is null)
            return Task.FromResult<WorldHistoryEntry?>(null);

        using SqliteCommand command = CreateCommand("DELETE FROM WorldHistory WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", entry.Id);
        command.ExecuteNonQuery();

        return Task.FromResult<WorldHistoryEntry?>(entry);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("SELECT COUNT(*) FROM WorldHistory;");
        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    private WorldHistoryEntry? Newest()
    {
        using SqliteCommand command = CreateCommand(
            $"SELECT {SelectColumns} FROM WorldHistory ORDER BY Id DESC LIMIT 1;");
        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? Entry(reader) : null;
    }

    private static WorldHistoryEntry Entry(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        SqliteValue.ToDate(reader.GetString(2)),
        reader.GetString(3),
        reader.GetString(4));
}
