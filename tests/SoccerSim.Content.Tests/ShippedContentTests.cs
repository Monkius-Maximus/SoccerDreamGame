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
    /// The bundle must reproduce the hand-written <c>sql/9999_seed_dev.sql</c> world exactly —
    /// same rows, same ids, same foreign keys. This is what makes deleting that seed file a safe,
    /// behaviour-preserving change rather than a leap of faith.
    /// </summary>
    [Fact]
    public void ShippedBundle_ReproducesTheLegacySqlSeed_RowForRow()
    {
        using var dir = new TempDirectory();

        string seedDbPath = Path.Combine(dir.Path, "seed.db");
        var seedFactory = SqliteConnectionFactory.ForFile(seedDbPath);
        new MigrationRunner(seedFactory).Migrate(includeSeeds: true);

        string contentDbPath = Path.Combine(dir.Path, "content.db");
        ContentDbBuilder.Build(ContentBundleFiles.Read(RepoPaths.ContentDir), contentDbPath);

        using SqliteConnection fromSeed = seedFactory.Open();
        using SqliteConnection fromContent = SqliteConnectionFactory.ForFile(contentDbPath).Open();

        foreach (string query in AuthoredWorldQueries)
            Assert.Equal(Dump(fromSeed, query), Dump(fromContent, query));
    }

    private static readonly string[] AuthoredWorldQueries =
    [
        "SELECT Id, Name, Country, Tier, CurrentSeasonId FROM Leagues ORDER BY Id;",
        "SELECT Id, Name, LeagueId, Budget, EloRating FROM Teams ORDER BY Id;",
        "SELECT Id, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, Shooting, "
        + "Tackling, Vision FROM Players ORDER BY Id;",
        "SELECT Id, Key, DisplayName, Aggression, Selfishness, EventWeightBias FROM PlayerTraits ORDER BY Id;",
        "SELECT PlayerId, TraitId FROM PlayerTraitAssignments ORDER BY PlayerId, TraitId;",
        "SELECT Id, Key, Name, Cost, StatKey, YieldMultiplier FROM HousingItems ORDER BY Id;",
        "SELECT Id, LeagueId, StartDate, EndDate FROM Seasons ORDER BY Id;",
        "SELECT Id, SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played FROM Matches ORDER BY Id;",
        "SELECT Id, HumanPlayerId FROM Career ORDER BY Id;",
        "SELECT PlayerId, Balance, BaseSalaryWeekly FROM PlayerFinances ORDER BY PlayerId;",
    ];

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

        return sql + Environment.NewLine + string.Join(Environment.NewLine, rows);
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
