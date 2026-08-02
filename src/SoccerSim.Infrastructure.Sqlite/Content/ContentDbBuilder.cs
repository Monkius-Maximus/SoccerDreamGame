using Microsoft.Data.Sqlite;
using SoccerSim.Content;

namespace SoccerSim.Infrastructure.Sqlite.Content;

/// <summary>
/// Turns a bundle into a ready-to-play SQLite file: create, migrate, import.
///
/// The result is a valid save database that simply has no play history yet, which is what lets
/// the game start a new career by copying bytes instead of running an import at boot.
/// </summary>
public static class ContentDbBuilder
{
    /// <summary>
    /// Builds <paramref name="outputPath"/> from <paramref name="bundle"/>, replacing any
    /// existing file. Returns what was imported.
    /// </summary>
    public static ContentImportReport Build(ContentBundle bundle, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Rebuild from scratch every time. An incremental build would have to reason about
        // rows the previous build left behind, and the file is a disposable artifact anyway.
        DeleteIfExists(outputPath);

        var factory = SqliteConnectionFactory.ForFile(outputPath);
        new MigrationRunner(factory).Migrate();

        using SqliteConnection connection = factory.Open();
        return new SqliteContentImporter(connection).EnsureImported(bundle);
    }

    private static void DeleteIfExists(string path)
    {
        // SQLite's WAL companions would otherwise be reapplied over the new file.
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string candidate = path + suffix;
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }
}
