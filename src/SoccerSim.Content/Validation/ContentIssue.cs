namespace SoccerSim.Content.Validation;

public enum IssueSeverity
{
    /// <summary>Worth a look, but the bundle still builds and imports.</summary>
    Warning,

    /// <summary>Blocks the export/import. The bundle would produce a broken world.</summary>
    Error,
}

/// <summary>
/// One validation finding, addressed precisely enough that the authoring UI can jump straight
/// to the offending cell: category + entity key + field.
/// </summary>
public sealed record ContentIssue
{
    public required IssueSeverity Severity { get; init; }

    /// <summary>Stable machine-readable code, e.g. <c>REF_DANGLING</c>. Tests assert on this.</summary>
    public required string Code { get; init; }

    public required string Category { get; init; }

    /// <summary>Key of the offending entity, or empty for a bundle-wide finding.</summary>
    public string EntityKey { get; init; } = string.Empty;

    public string Field { get; init; } = string.Empty;

    public required string Message { get; init; }

    public override string ToString()
    {
        string location = string.IsNullOrEmpty(EntityKey) ? Category : $"{Category}/{EntityKey}";
        if (!string.IsNullOrEmpty(Field))
            location += $".{Field}";
        return $"{Severity.ToString().ToUpperInvariant()} [{Code}] {location}: {Message}";
    }
}

/// <summary>The outcome of a validation pass.</summary>
public sealed record ContentValidationResult
{
    public required IReadOnlyList<ContentIssue> Issues { get; init; }

    public IEnumerable<ContentIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<ContentIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);

    public bool IsValid => !Errors.Any();

    /// <summary>Throws if anything blocking was found, listing every error rather than just the first.</summary>
    public void ThrowIfInvalid()
    {
        if (IsValid)
            return;

        string detail = string.Join(Environment.NewLine, Errors.Select(e => "  " + e));
        throw new ContentValidationException(
            $"Content validation failed with {Errors.Count()} error(s):{Environment.NewLine}{detail}");
    }
}

public sealed class ContentValidationException : Exception
{
    public ContentValidationException(string message) : base(message)
    {
    }
}
