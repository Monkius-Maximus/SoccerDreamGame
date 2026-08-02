namespace SoccerSim.Content;

/// <summary>
/// The bundle's index: versions, provenance, and a content hash so a save can tell whether it
/// already holds this exact build.
/// </summary>
public sealed record ContentManifest
{
    public int FormatVersion { get; init; } = ContentSchema.FormatVersion;

    public int ContentVersion { get; init; } = ContentSchema.CurrentVersion;

    /// <summary>ISO-8601 UTC timestamp identifying this build, for logs and error messages.</summary>
    public string BuildId { get; init; } = string.Empty;

    public string Generator { get; init; } = string.Empty;

    /// <summary>
    /// SHA-256 over the canonical serialization of the payload — NOT over the generated
    /// <c>content.db</c>, whose page layout and freelists vary between writes and would make
    /// an otherwise-identical build look changed.
    /// </summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>Row count per category, for the boot log and the tool's export summary.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();
}
