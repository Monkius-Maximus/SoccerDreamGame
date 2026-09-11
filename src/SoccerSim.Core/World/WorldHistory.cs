using SoccerSim.Core.Persistence;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Serialization;

namespace SoccerSim.Core.World;

/// <summary>
/// The undo stack, in front of every write the tool makes (ROADMAP.md Sprint 9).
///
/// <para>The tool has no Save button — every edit lands immediately, which is right for authoring
/// and wrong for a mistake. Three paths are openly destructive (regenerating a squad, applying a
/// CSV import that removes rows, recalculating the batch) and a <c>confirm()</c> asks a question
/// nobody can answer without seeing the result. This is the answer: do it, look, undo.</para>
///
/// <para>A snapshot is the whole world document, not a diff. It is the format the exporter writes
/// and the importer reads, and a test already pins that the two are inverses — so undo is correct
/// by construction instead of by a second mechanism nobody exercises. A diff-based stack would
/// need an inverse for every operation, including "replace the whole world from CSV", and those
/// inverses are exactly where an undo stack goes wrong quietly.</para>
/// </summary>
public static class WorldHistory
{
    /// <summary>How many steps back the stack holds. The prototype's number, and a sound one: a
    /// deeper stack is not more useful, it is more storage for a session nobody remembers.</summary>
    public const int Depth = 25;

    /// <summary>
    /// Records what the world looks like right now, labelled with what is about to happen to it.
    /// Called at the top of every write, before anything changes.
    /// </summary>
    public static async Task RecordAsync(
        IWorldUnitOfWork unitOfWork,
        string label,
        CancellationToken cancellationToken = default)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        await unitOfWork.History.PushAsync(label, WorldJsonWriter.Write(world), Depth, cancellationToken);
    }

    /// <summary>
    /// Puts the newest snapshot back and removes it from the stack. Returns the label of what was
    /// undone, or null when there is nothing to undo.
    /// </summary>
    public static async Task<string?> UndoAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        WorldHistoryEntry? entry = await unitOfWork.History.PopAsync(cancellationToken);
        if (entry is null)
            return null;

        // Read through the same reader every import uses, so a snapshot that cannot be read is a
        // loud failure here rather than a half-restored world.
        WorldSnapshot restored = WorldJsonReader.Read(entry.Document);

        await WorldStore.ReplaceAsync(unitOfWork, restored, cancellationToken);

        return entry.Label;
    }
}
