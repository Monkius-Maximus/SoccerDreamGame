using Microsoft.Data.Sqlite;
using SoccerSim.Infrastructure.Sqlite;
using SoccerSim.Infrastructure.Sqlite.Content;
using Xunit;

namespace SoccerSim.Content.Tests;

/// <summary>
/// The authoring loop's safety net. Editing content and relaunching leaves the previous save
/// holding a different build, which cannot be reconciled in place — so a save with nothing
/// played is rebuilt, and a save with real progress is never silently discarded.
/// </summary>
public sealed class SaveDatabaseTests
{
    [Fact]
    public void Open_ImportsContent_IntoAFreshFile()
    {
        ContentBundle bundle = TestBundles.Minimal();
        using var dir = new TempDirectory();

        using SaveDatabase save = SaveDatabase.Open(SavePath(dir), bundle);

        Assert.False(save.Import.AlreadyPresent);
        Assert.False(save.WasReset);
        Assert.Equal(2L, TeamCount(save.Connection));
    }

    [Fact]
    public void Open_IsANoOp_WhenTheSaveAlreadyHoldsThisContent()
    {
        ContentBundle bundle = TestBundles.Minimal();
        using var dir = new TempDirectory();
        string path = SavePath(dir);

        SaveDatabase.Open(path, bundle).Dispose();
        using SaveDatabase reopened = SaveDatabase.Open(path, bundle);

        Assert.True(reopened.Import.AlreadyPresent);
        Assert.False(reopened.WasReset);
    }

    [Fact]
    public void Open_RebuildsTheSave_WhenContentChanged_AndNothingHasBeenPlayed()
    {
        using var dir = new TempDirectory();
        string path = SavePath(dir);

        SaveDatabase.Open(path, TestBundles.Minimal()).Dispose();

        ContentBundle edited = Edited(TestBundles.Minimal());
        var log = new List<string>();

        using SaveDatabase reopened =
            SaveDatabase.Open(path, edited, resetWhenContentChanged: true, log: log.Add);

        Assert.True(reopened.WasReset);
        Assert.False(reopened.Import.AlreadyPresent);
        Assert.Equal(edited.Manifest.ContentHash, ContentBuildInfo.Read(reopened.Connection)!.ContentHash);

        // The career is gone; saying so is the whole point of the log hook.
        string message = Assert.Single(log);
        Assert.Contains(path, message, StringComparison.Ordinal);
        Assert.Contains(edited.Manifest.ContentHash, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_Throws_WhenContentChanged_AndMatchesHaveBeenPlayed()
    {
        using var dir = new TempDirectory();
        string path = SavePath(dir);

        using (SaveDatabase seeded = SaveDatabase.Open(path, TestBundles.Minimal()))
        {
            MarkFirstMatchPlayed(seeded.Connection);
            Assert.True(SaveProgress.HasPlayedMatches(seeded.Connection));
        }

        ContentBundle edited = Edited(TestBundles.Minimal());

        // resetWhenContentChanged is on, and it still must not delete this file: progress
        // outranks the convenience of the authoring loop.
        Assert.Throws<ContentBuildMismatchException>(
            () => SaveDatabase.Open(path, edited, resetWhenContentChanged: true));

        Assert.True(File.Exists(path));

        using SaveDatabase survivor = SaveDatabase.Open(path, TestBundles.Minimal());
        Assert.True(survivor.Import.AlreadyPresent);
        Assert.True(SaveProgress.HasPlayedMatches(survivor.Connection));
    }

    [Fact]
    public void Open_Throws_WhenContentChanged_AndResetIsDisabled()
    {
        using var dir = new TempDirectory();
        string path = SavePath(dir);

        SaveDatabase.Open(path, TestBundles.Minimal()).Dispose();

        Assert.Throws<ContentBuildMismatchException>(
            () => SaveDatabase.Open(path, Edited(TestBundles.Minimal())));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void HasPlayedMatches_IsFalse_ForAFreshlyImportedWorld()
    {
        using var dir = new TempDirectory();

        using SaveDatabase save = SaveDatabase.Open(SavePath(dir), TestBundles.Minimal());

        Assert.False(SaveProgress.HasPlayedMatches(save.Connection));
    }

    [Fact]
    public void Delete_RemovesTheWalAndSharedMemorySidecars()
    {
        using var dir = new TempDirectory();
        string path = SavePath(dir);

        SaveDatabase.Open(path, TestBundles.Minimal()).Dispose();
        File.WriteAllText(path + "-wal", "stale");
        File.WriteAllText(path + "-shm", "stale");

        SaveDatabase.Delete(path);

        // A leftover WAL would replay committed pages of the old world into the new file.
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + "-wal"));
        Assert.False(File.Exists(path + "-shm"));
    }

    /// <summary>The same world under a different content hash — what editing in the tool produces.</summary>
    private static ContentBundle Edited(ContentBundle bundle) => bundle with
    {
        Manifest = bundle.Manifest with { ContentHash = "sha256:edited-in-the-tool", BuildId = "edited" },
    };

    private static string SavePath(TempDirectory dir) => Path.Combine(dir.Path, "save.db");

    private static long TeamCount(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Teams;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void MarkFirstMatchPlayed(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Matches SET Played = 1, HomeGoals = 2, AwayGoals = 1 "
            + "WHERE Id = (SELECT MIN(Id) FROM Matches);";
        Assert.Equal(1, command.ExecuteNonQuery());
    }
}
