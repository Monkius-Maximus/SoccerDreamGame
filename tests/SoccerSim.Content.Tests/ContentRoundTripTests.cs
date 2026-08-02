using Microsoft.Data.Sqlite;
using SoccerSim.Content.Serialization;
using SoccerSim.Infrastructure.Sqlite;
using SoccerSim.Infrastructure.Sqlite.Content;
using Xunit;

namespace SoccerSim.Content.Tests;

/// <summary>
/// The pipeline's load-bearing guarantees: what goes in comes out, through JSON and through
/// SQLite. Without these, a column silently dropped in one direction would only surface as
/// missing content in a shipped build.
/// </summary>
public sealed class ContentRoundTripTests
{
    [Fact]
    public void Json_RoundTrips_EveryCategory()
    {
        ContentBundle original = TestBundles.Minimal();
        using var dir = new TempDirectory();

        ContentBundleFiles.Write(original, dir.Path, "tests");
        ContentBundle reloaded = ContentBundleFiles.Read(dir.Path);

        // Compare canonical JSON, not object graphs: records compare their IReadOnlyList members
        // by reference, so Assert.Equal on the bundles would pass even if every list were empty.
        // The manifest is regenerated on write (new BuildId, freshly computed hash), so the
        // payload is what has to round-trip.
        Assert.Equal(CanonicalPayload(original), CanonicalPayload(reloaded));
    }

    [Fact]
    public void Json_WritesEntitiesSortedByKey_SoDiffsStayReadable()
    {
        ContentBundle bundle = TestBundles.Minimal();
        using var dir = new TempDirectory();

        ContentBundleFiles.Write(bundle, dir.Path, "tests");
        string teams = File.ReadAllText(Path.Combine(dir.Path, "teams.json"));

        Assert.True(
            teams.IndexOf("alpha-fc", StringComparison.Ordinal)
            < teams.IndexOf("beta-united", StringComparison.Ordinal),
            "Entities must be written in ordinal key order so a diff shows only real changes.");
    }

    [Fact]
    public void Json_ReExport_ProducesTheSameContentHash()
    {
        ContentBundle bundle = TestBundles.Minimal();
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        ContentManifest a = ContentBundleFiles.Write(bundle, first.Path, "tests");
        ContentManifest b = ContentBundleFiles.Write(bundle, second.Path, "tests");

        // The hash covers content only. BuildId differs between the two exports, and must not
        // leak into the hash, or every rebuild would look like a content change.
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.NotEqual(string.Empty, a.ContentHash);
    }

    [Fact]
    public void Sqlite_RoundTrips_EveryCategory()
    {
        ContentBundle original = TestBundles.Minimal();
        using var dir = new TempDirectory();
        string dbPath = Path.Combine(dir.Path, "content.db");

        ContentDbBuilder.Build(original, dbPath);

        using SqliteConnection connection = SqliteConnectionFactory.ForFile(dbPath).Open();
        ContentBundle reloaded = new SqliteContentReader(connection).Read();

        // The manifest is regenerated from the ContentBuilds stamp, so compare the payload only.
        Assert.Equal(CanonicalPayload(original), CanonicalPayload(reloaded));
    }

    [Fact]
    public void Sqlite_PreservesAuthoredIds()
    {
        ContentBundle original = TestBundles.Minimal();
        using var dir = new TempDirectory();
        string dbPath = Path.Combine(dir.Path, "content.db");

        ContentDbBuilder.Build(original, dbPath);

        using SqliteConnection connection = SqliteConnectionFactory.ForFile(dbPath).Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Teams WHERE Key = 'alpha-fc';";

        // Ids are foreign keys in every save file. If the importer let SQLite assign them,
        // a rebuild would silently repoint Matches, Standings and Career at other rows.
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void EnsureImported_IsNoOp_WhenTheSameBuildIsAlreadyPresent()
    {
        ContentBundle bundle = TestBundles.Minimal();
        using var dir = new TempDirectory();
        string dbPath = Path.Combine(dir.Path, "content.db");

        ContentDbBuilder.Build(bundle, dbPath);

        using SqliteConnection connection = SqliteConnectionFactory.ForFile(dbPath).Open();
        ContentImportReport second = new SqliteContentImporter(connection).EnsureImported(bundle);

        Assert.True(second.AlreadyPresent);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Teams;";
        Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void EnsureImported_Throws_WhenADifferentBuildWouldOverwriteAnExistingSave()
    {
        ContentBundle bundle = TestBundles.Minimal();
        using var dir = new TempDirectory();
        string dbPath = Path.Combine(dir.Path, "content.db");

        ContentDbBuilder.Build(bundle, dbPath);

        ContentBundle other = bundle with
        {
            Manifest = bundle.Manifest with { ContentHash = "sha256:something-else", BuildId = "other" },
        };

        using SqliteConnection connection = SqliteConnectionFactory.ForFile(dbPath).Open();
        var importer = new SqliteContentImporter(connection);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => importer.EnsureImported(other));
        Assert.Contains("not supported", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Everything except the manifest, which is provenance rather than content.</summary>
    private static string CanonicalPayload(ContentBundle bundle)
        => ContentJson.Serialize(bundle with { Manifest = new ContentManifest() });
}
