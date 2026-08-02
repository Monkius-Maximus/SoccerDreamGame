using Microsoft.Data.Sqlite;
using SoccerSim.Content.Serialization;
using SoccerSim.Content.Validation;
using SoccerSim.Infrastructure.Sqlite;
using SoccerSim.Infrastructure.Sqlite.Content;
using Xunit;

namespace SoccerSim.Content.Tests;

/// <summary>
/// The one place that asserts on the repository's real bundle. Kept apart from the unit tests
/// on purpose: those use fixtures, so editing content can never break them, while these catch
/// a bundle that was hand-edited and never re-exported.
/// </summary>
public sealed class ShippedContentTests
{
    [Fact]
    public void ShippedBundle_Validates()
    {
        ContentBundle bundle = ContentBundleFiles.Read(RepoPaths.ContentDir);
        ContentValidationResult result = ContentValidator.Default.Validate(bundle);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void ShippedBundle_HashMatchesItsManifest()
    {
        // Fails when someone edits a JSON file by hand without re-running the export, which
        // would otherwise stamp saves with a hash that does not describe their contents.
        Assert.True(
            ContentBundleFiles.HashMatches(RepoPaths.ContentDir),
            $"{RepoPaths.ContentDir} content hash does not match manifest.json. Re-export the bundle.");
    }

    /// <summary>
    /// The shipped bundle must still produce the exact world the retired
    /// <c>sql/9999_seed_dev.sql</c> did — the ids here are the ones the LOD tests, the career
    /// service and the fixture gateway all assert against, and they are foreign keys in every
    /// save file. Pinned explicitly, because "the content happens to be right" is not something
    /// the other tests would notice going wrong.
    /// </summary>
    [Fact]
    public void ShippedBundle_BuildsTheExpectedWorld()
    {
        using var dir = new TempDirectory();
        string dbPath = Path.Combine(dir.Path, "content.db");
        ContentDbBuilder.Build(ContentBundleFiles.Read(RepoPaths.ContentDir), dbPath);

        using SqliteConnection connection = SqliteConnectionFactory.ForFile(dbPath).Open();

        Assert.Equal(
            "1 | Premier Division | Homeland | 1\n"
            + "2 | La Liga Mayor | Iberia | 2\n"
            + "3 | Regional North | Homeland | 3",
            Dump(connection, "SELECT Id, Name, Country, Tier FROM Leagues ORDER BY Id;"));

        Assert.Equal(
            "1 | Riverside FC | 1\n2 | Hilltop United | 1\n3 | Costa Real | 2\n"
            + "4 | Atletico Sur | 2\n5 | North Rovers | 3\n6 | Lakeside Town | 3",
            Dump(connection, "SELECT Id, Name, LeagueId FROM Teams ORDER BY Id;"));

        // The human is player 1 at club 1, which is what SqliteCareerService resolves against.
        Assert.Equal("1 | 1", Dump(connection, "SELECT Id, HumanPlayerId FROM Career;"));
        Assert.Equal("1 | Alex | Mercer | 1",
            Dump(connection, "SELECT Id, FirstName, LastName, TeamId FROM Players WHERE Id = 1;"));

        // Two unplayed Tier 1 fixtures, so "Play Next Fixture" has something to render.
        Assert.Equal(
            "1 | 1 | 1 | 2 | 2026-08-08 15:00:00\n2 | 1 | 2 | 1 | 2026-08-15 15:00:00",
            Dump(connection,
                "SELECT Id, LeagueId, HomeTeamId, AwayTeamId, KickoffDate FROM Matches ORDER BY Id;"));
    }

    private static string Dump(SqliteConnection connection, string sql)
    {
        var rows = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            var values = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                values[i] = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString() ?? string.Empty;
            rows.Add(string.Join(" | ", values));
        }

        return string.Join("\n", rows);
    }
}

internal static class RepoPaths
{
    /// <summary>
    /// The repository's committed bundle. Located by walking up from the test binary to the
    /// solution file, so it works from any working directory the runner happens to use.
    /// </summary>
    public static string ContentDir { get; } = Path.Combine(Root, "content", "dev");

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SoccerDreamGame.sln")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException(
                       "Could not locate the repository root (no SoccerDreamGame.sln above "
                       + AppContext.BaseDirectory + ").");
        }
    }
}
