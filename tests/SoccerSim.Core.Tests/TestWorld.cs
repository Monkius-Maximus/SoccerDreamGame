using Microsoft.Data.Sqlite;
using SoccerSim.Content;
using SoccerSim.Content.Serialization;
using SoccerSim.Infrastructure.Sqlite;
using SoccerSim.Infrastructure.Sqlite.Content;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Builds the test world the same way the game does: migrate, then import the authored content
/// bundle. This replaced <c>Migrate(includeSeeds: true)</c> when the hand-written
/// <c>sql/9999_seed_dev.sql</c> was retired.
///
/// The bundle produces the identical rows and ids the old seed did — clubs 1–6, players 1–12,
/// Alex Mercer as the human — so every assertion below is unchanged from when it was SQL.
/// </summary>
internal static class TestWorld
{
    /// <summary>The repository's committed content bundle.</summary>
    public static string ContentDirectory { get; } = Path.Combine(RepositoryRoot, "content", "dev");

    // Parsed once for the whole suite; every test then imports the same in-memory bundle.
    private static readonly Lazy<ContentBundle> Shared = new(() => ContentBundleFiles.Read(ContentDirectory));

    /// <summary>
    /// A migrated in-memory database, optionally populated from the content bundle.
    ///
    /// A shared in-memory database lives only while at least one connection is open, so the
    /// caller must hold the returned keep-alive for the duration of the test.
    /// </summary>
    public static (SqliteConnectionFactory Factory, SqliteConnection KeepAlive) New(bool withContent)
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate();

        if (withContent)
            new SqliteContentImporter(keepAlive).EnsureImported(Shared.Value);

        return (factory, keepAlive);
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SoccerDreamGame.sln")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException(
                       "Could not locate the repository root above " + AppContext.BaseDirectory + ".");
        }
    }
}
