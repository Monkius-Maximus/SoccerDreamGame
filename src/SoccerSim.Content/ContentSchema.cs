namespace SoccerSim.Content;

/// <summary>Version constants for the content format, and the compatibility rules between them.</summary>
public static class ContentSchema
{
    /// <summary>
    /// Shape of the JSON envelope itself (file layout, manifest fields). Bumped only when the
    /// container changes, not when an entity gains a field.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// The content model version. Bumped whenever an entity gains or loses a field, so a game
    /// binary can tell whether it understands a bundle it did not generate.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Oldest content version this build can still read. Moves only when a change cannot be
    /// back-filled with a default.
    /// </summary>
    public const int MinSupportedVersion = 1;
}

/// <summary>Result of comparing a bundle's declared versions against what this build supports.</summary>
public enum ContentCompatibility
{
    Compatible,

    /// <summary>The bundle was written by a newer build. Refuse — we cannot guess the new fields.</summary>
    ContentTooNew,

    /// <summary>The bundle predates <see cref="ContentSchema.MinSupportedVersion"/>. Rebuild it.</summary>
    ContentTooOld,

    /// <summary>The JSON envelope itself is a shape we do not know.</summary>
    FormatUnsupported,
}

public static class ContentCompatibilityCheck
{
    public static ContentCompatibility Check(int formatVersion, int contentVersion)
    {
        if (formatVersion != ContentSchema.FormatVersion)
            return ContentCompatibility.FormatUnsupported;
        if (contentVersion > ContentSchema.CurrentVersion)
            return ContentCompatibility.ContentTooNew;
        if (contentVersion < ContentSchema.MinSupportedVersion)
            return ContentCompatibility.ContentTooOld;
        return ContentCompatibility.Compatible;
    }

    /// <summary>
    /// Fail-fast guard matching the house style (see <c>MatchEntryGuard</c>): an incompatible
    /// bundle throws with a message that says what to do about it, rather than loading a
    /// half-understood world.
    /// </summary>
    public static void Validate(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        ContentCompatibility result = Check(manifest.FormatVersion, manifest.ContentVersion);
        if (result == ContentCompatibility.Compatible)
            return;

        string detail = result switch
        {
            ContentCompatibility.FormatUnsupported =>
                $"bundle format version {manifest.FormatVersion} is not readable by this build "
                + $"(expected {ContentSchema.FormatVersion}).",
            ContentCompatibility.ContentTooNew =>
                $"content version {manifest.ContentVersion} is newer than this build understands "
                + $"({ContentSchema.CurrentVersion}). Update the game, or rebuild the content from this branch.",
            ContentCompatibility.ContentTooOld =>
                $"content version {manifest.ContentVersion} is older than the minimum supported "
                + $"({ContentSchema.MinSupportedVersion}). Re-export the bundle with the current tool.",
            _ => result.ToString(),
        };

        throw new ContentFormatException($"Content build '{manifest.BuildId}': {detail}");
    }
}

/// <summary>Thrown when a bundle cannot be read or is incompatible with this build.</summary>
public sealed class ContentFormatException : Exception
{
    public ContentFormatException(string message) : base(message)
    {
    }

    public ContentFormatException(string message, Exception inner) : base(message, inner)
    {
    }
}
