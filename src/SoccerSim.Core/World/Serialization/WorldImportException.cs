namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// Thrown when a world document contains malformed records. Carries EVERY problem found, not
/// just the first: this is a data-cleanup tool, and being told about one bad row at a time turns
/// a five-minute fix into twenty round trips.
///
/// <para>
/// Nothing is written when this is thrown. The reader produces a whole
/// <see cref="WorldSnapshot"/> or nothing at all, so a partially-imported database is not a
/// state the importer can reach (ROADMAP.md Sprint 2: "Rejeitar registro malformado com
/// mensagem, nunca gravar").
/// </para>
/// </summary>
public sealed class WorldImportException : Exception
{
    public WorldImportException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
        => Errors = errors;

    /// <summary>One path-qualified message per malformed record, e.g.
    /// <c>geoNodes[7]: missing required field 'kind'</c>.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        // "problem", not "malformed record": the same exception also reports a refused import
        // (e.g. a database that already holds a world), which is not a bad record.
        string count = errors.Count == 1 ? "1 problem" : $"{errors.Count} problems";
        return string.Join(Environment.NewLine, [$"World import rejected: {count}. Nothing was written.", .. errors]);
    }
}

/// <summary>A single malformed field, identified by its path in the document. Internal to the
/// reader, which collects these into a <see cref="WorldImportException"/>.</summary>
internal sealed class WorldFieldException : Exception
{
    public WorldFieldException(string path, string reason)
        : base($"{path}: {reason}")
    {
    }
}
