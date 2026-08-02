using Xunit;

namespace SoccerSim.Content.Tests;

/// <summary>
/// Version-skew behaviour. A bundle the build does not fully understand must be refused
/// outright — loading it partially would produce a world that is silently missing content.
/// </summary>
public sealed class ContentCompatibilityTests
{
    [Fact]
    public void CurrentVersions_AreCompatible()
    {
        Assert.Equal(
            ContentCompatibility.Compatible,
            ContentCompatibilityCheck.Check(ContentSchema.FormatVersion, ContentSchema.CurrentVersion));
    }

    [Fact]
    public void NewerContentVersion_IsRefused()
    {
        Assert.Equal(
            ContentCompatibility.ContentTooNew,
            ContentCompatibilityCheck.Check(ContentSchema.FormatVersion, ContentSchema.CurrentVersion + 1));
    }

    [Fact]
    public void UnknownFormatVersion_IsRefused()
    {
        Assert.Equal(
            ContentCompatibility.FormatUnsupported,
            ContentCompatibilityCheck.Check(ContentSchema.FormatVersion + 1, ContentSchema.CurrentVersion));
    }

    [Fact]
    public void Validate_Throws_WithAnActionableMessage_ForContentFromANewerBuild()
    {
        var manifest = new ContentManifest
        {
            BuildId = "2026-08-02T00:00:00Z",
            ContentVersion = ContentSchema.CurrentVersion + 1,
        };

        ContentFormatException error =
            Assert.Throws<ContentFormatException>(() => ContentCompatibilityCheck.Validate(manifest));

        Assert.Contains(manifest.BuildId, error.Message, StringComparison.Ordinal);
        Assert.Contains("Update the game", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Passes_ForACurrentManifest()
    {
        ContentCompatibilityCheck.Validate(new ContentManifest { BuildId = "current" });
    }
}
