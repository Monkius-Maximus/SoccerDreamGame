using Microsoft.Data.Sqlite;
using SoccerSim.Content;
using SoccerSim.Infrastructure.Sqlite.Content;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// Opens the game's save database and brings it in line with the content bundle this
/// build ships: migrate the schema, then import the world unless it is already present.
///
/// This lives here rather than in the Godot autoload so the whole sequence — including
/// the interesting case, where the authored content changed under an existing save —
/// runs and is tested headlessly.
/// </summary>
public sealed class SaveDatabase : IDisposable
{
    private SaveDatabase(SqliteConnection connection, ContentImportReport import, bool wasReset)
    {
        Connection = connection;
        Import = import;
        WasReset = wasReset;
    }

    /// <summary>The live connection, owned by this instance and closed on dispose.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>What the content import did (or found already present).</summary>
    public ContentImportReport Import { get; }

    /// <summary>
    /// True if the previous save file was discarded because it held different content.
    /// Callers should surface this: the player's career is gone.
    /// </summary>
    public bool WasReset { get; }

    /// <summary>
    /// Migrates and opens the save at <paramref name="path"/>, importing
    /// <paramref name="content"/> if the save does not already hold it.
    ///
    /// A save holding *different* content cannot be reconciled in place — the ids that
    /// Matches, Standings and Career point at would be renumbered underneath them. When
    /// <paramref name="resetWhenContentChanged"/> is true (the authoring loop: edit content,
    /// rebuild, run) such a save is deleted and rebuilt, but only if nothing has been played
    /// yet. A save with real progress always throws instead, whatever the flag says — a lost
    /// career is not something to trade for convenience.
    /// </summary>
    public static SaveDatabase Open(
        string path,
        ContentBundle content,
        bool resetWhenContentChanged = false,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var factory = SqliteConnectionFactory.ForFile(path);

        SaveDatabase? opened = TryOpen(factory, content, wasReset: false, out ContentBuildMismatchException? mismatch);
        if (opened is not null)
            return opened;

        // Reached only on a content mismatch that TryOpen judged recoverable.
        log?.Invoke(
            $"Authored content changed ({mismatch!.ExistingContentHash} -> {mismatch.IncomingContentHash}). "
            + $"No matches had been played, so the save at '{path}' was rebuilt from the new content.");

        Delete(path);
        return TryOpen(factory, content, wasReset: true, out _)
               ?? throw new InvalidOperationException(
                   $"The save at '{path}' still rejected the content bundle after being rebuilt.");

        // Opens and imports; returns null (with the mismatch) only when the caller may retry
        // on a fresh file, i.e. the content changed and the save holds no played matches.
        SaveDatabase? TryOpen(
            SqliteConnectionFactory f,
            ContentBundle bundle,
            bool wasReset,
            out ContentBuildMismatchException? recoverable)
        {
            recoverable = null;
            new MigrationRunner(f).Migrate();
            SqliteConnection connection = f.Open();

            try
            {
                ContentImportReport import = new SqliteContentImporter(connection).EnsureImported(bundle);
                return new SaveDatabase(connection, import, wasReset);
            }
            catch (ContentBuildMismatchException error)
                when (resetWhenContentChanged && !wasReset && !SaveProgress.HasPlayedMatches(connection))
            {
                // Filter runs before the connection is disposed, so the progress check is valid.
                recoverable = error;
                connection.Dispose();
                return null;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }

    /// <summary>Deletes a save file and the WAL/shared-memory sidecars SQLite leaves beside it.</summary>
    public static void Delete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Pooled connections keep an OS handle on the file; on Windows the delete fails
        // outright without this, and elsewhere a stale pooled handle would resurrect the WAL.
        SqliteConnection.ClearAllPools();

        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    public void Dispose() => Connection.Dispose();
}

/// <summary>Whether a save holds player progress worth protecting from a content reset.</summary>
public static class SaveProgress
{
    /// <summary>
    /// True once any fixture has been resolved. Played matches are the earliest durable
    /// trace of a career: standings, form and finances all follow from them, and a save
    /// with none is indistinguishable from a freshly imported world.
    /// </summary>
    public static bool HasPlayedMatches(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Matches WHERE Played = 1);";
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }
}
