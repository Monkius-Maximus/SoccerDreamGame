using SoccerSim.Content.Serialization;
using Xunit;

namespace SoccerSim.Content.Tests;

/// <summary>
/// The shipped game loads its bundle from embedded assembly resources, not from disk. That path
/// has no other coverage — in Godot it only runs at startup in an exported build, which is
/// exactly where a wrong resource name is most expensive to discover.
/// </summary>
public sealed class EmbeddedBundleTests
{
    private const string ResourcePrefix = "SoccerDreamGame.content";

    [Fact]
    public void EmbeddedBundle_LoadsAndMatchesTheOneOnDisk()
    {
        ContentBundle fromResources =
            ContentBundleResources.Read(typeof(EmbeddedBundleTests).Assembly, ResourcePrefix);
        ContentBundle fromDisk = ContentBundleFiles.Read(RepoPaths.ContentDir);

        Assert.Equal(fromDisk.Manifest.ContentHash, fromResources.Manifest.ContentHash);
        Assert.Equal(
            ContentJson.Serialize(fromDisk with { Manifest = new ContentManifest() }),
            ContentJson.Serialize(fromResources with { Manifest = new ContentManifest() }));
    }

    [Fact]
    public void MissingBundle_ThrowsAnActionableError_RatherThanReturningAnEmptyWorld()
    {
        ContentFormatException error = Assert.Throws<ContentFormatException>(
            () => ContentBundleResources.Read(typeof(EmbeddedBundleTests).Assembly, "No.Such.Prefix"));

        Assert.Contains(ContentBundleFiles.ManifestFileName, error.Message, StringComparison.Ordinal);
    }
}
