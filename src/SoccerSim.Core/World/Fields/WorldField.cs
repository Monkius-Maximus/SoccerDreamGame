namespace SoccerSim.Core.World.Fields;

/// <summary>
/// Where a value came from — the taxonomy from the consolidated document (DATA_CONTRACT.md §3,
/// column P). The tool shows this as a seal beside every field, because "who decided this" is
/// the question the whole project exists to answer.
/// </summary>
public enum FieldProvenance
{
    /// <summary>A person chose it.</summary>
    Authored,

    /// <summary>Followed from other authored values by a documented rule.</summary>
    Derived,

    /// <summary>Drawn from an observed distribution.</summary>
    Sampled,

    /// <summary>Computed by a formula and never stored by hand.</summary>
    Calculated,
}

/// <summary>How a field is entered and validated.</summary>
public enum FieldKind
{
    Text,
    LongText,
    Int,
    Float,
    /// <summary>A 7-character hex colour, "#RRGGBB".</summary>
    Color,
    /// <summary>One of a closed enum's names; <see cref="WorldField.EnumName"/> says which.</summary>
    Enum,
    /// <summary>An id pointing at another record (a club, a geo node).</summary>
    Reference,
}

/// <summary>
/// One editable or displayable field of a record, addressed by its path
/// (<c>kits.home.shirt</c>).
///
/// <para>
/// This catalog is the single source for three things that must never disagree: what the server
/// accepts in a PATCH, how the form renders the field, and which seal it carries. The prototype
/// kept the same list (CLUB_SPEC) for the same reason — a form built from one list and validated
/// against another drifts silently.
/// </para>
/// </summary>
public sealed record WorldField(
    string Path,
    string Label,
    FieldKind Kind,
    FieldProvenance Provenance,
    bool Editable = true,
    string? EnumName = null);

public sealed record WorldFieldGroup(string Name, string Id, IReadOnlyList<WorldField> Fields);

/// <summary>Thrown when a patch names a field that does not exist, cannot be edited, or carries a
/// value the field does not accept. The message is shown to the user, so it says what was wrong
/// and — for a closed enum — what would have been right.</summary>
public sealed class FieldPatchException : Exception
{
    public FieldPatchException(string path, string reason)
        : base($"{path}: {reason}")
        => Path = path;

    public string Path { get; }
}
