using SoccerSim.Core.Persistence;
using SoccerSim.Core.World.Serialization;

namespace SoccerSim.Core.World.Import;

/// <summary>What an import wrote, for the command line to report.</summary>
public sealed record WorldImportReport(
    int GeoNodes,
    int Clubs,
    int Characters,
    int Competitions,
    int Sources)
{
    public override string ToString() =>
        $"{GeoNodes} geo nodes, {Clubs} clubs, {Characters} characters, " +
        $"{Competitions} competitions, {Sources} sources";
}

/// <summary>
/// Loads a world document into the database. Deliberately an OPERATION, not a migration
/// (ROADMAP.md Sprint 2): importing data is something a user does on purpose to a database that
/// already has a schema, and migrations that carry data can't be re-run or pointed at a
/// different file.
///
/// <para>
/// Order matters and is a foreign-key order: geo nodes before the clubs that sit in them,
/// calibration before the clubs and characters whose derived fields are computed from it, clubs
/// before their characters and before the competitions that list them as members. The whole
/// import runs in one transaction — a failure halfway leaves the database exactly as it was.
/// </para>
/// </summary>
public sealed class WorldImporter
{
    private readonly IWorldUnitOfWork _unitOfWork;

    public WorldImporter(IWorldUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    /// <summary>Parses and imports a world document. Throws <see cref="WorldImportException"/>
    /// listing every malformed record, without writing anything, if the document is bad.</summary>
    public async Task<WorldImportReport> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        // Parse first, write second: a malformed document must never reach the database.
        WorldSnapshot snapshot = WorldJsonReader.Read(json);
        return await ImportAsync(snapshot, cancellationToken);
    }

    public async Task<WorldImportReport> ImportAsync(WorldSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await GuardAgainstOverwritingAnExistingWorldAsync(cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Parents before children: a node's parent must exist before it is inserted.
            foreach (GeoNode node in OrderByHierarchy(snapshot.GeoNodes))
                await _unitOfWork.GeoNodes.AddAsync(node, cancellationToken);

            // Before clubs and characters: their derived fields are computed from it.
            await _unitOfWork.Calibration.SaveAsync(snapshot.Calibration, cancellationToken);

            // The world's own facts about itself, including the seed generation derives from.
            await _unitOfWork.Settings.SetAsync(
                WorldMeta.MasterSeedKey, snapshot.Meta.MasterSeed.ToString(), cancellationToken);
            await _unitOfWork.Settings.SetAsync(
                WorldMeta.SchemaVersionKey, snapshot.Meta.SchemaVersion, cancellationToken);
            if (snapshot.Meta.SourceFile is { } sourceFile)
                await _unitOfWork.Settings.SetAsync(WorldMeta.SourceFileKey, sourceFile, cancellationToken);

            foreach (ClubIdentity club in snapshot.Clubs)
                await _unitOfWork.Clubs.AddAsync(club, cancellationToken);

            foreach (CharacterRecord character in snapshot.Characters)
                await _unitOfWork.Characters.AddAsync(character, cancellationToken);

            foreach (Competition competition in snapshot.Competitions)
                await _unitOfWork.Competitions.AddAsync(competition, cancellationToken);

            await _unitOfWork.Sources.ReplaceAllAsync(snapshot.Sources, cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return new WorldImportReport(
            snapshot.GeoNodes.Count,
            snapshot.Clubs.Count,
            snapshot.Characters.Count,
            snapshot.Competitions.Count,
            snapshot.Sources.Count);
    }

    /// <summary>
    /// Importing into a database that already holds a world is refused. It is not a merge — the
    /// rows would collide on their primary keys and the user would get a UNIQUE constraint error
    /// naming a geo node, which says nothing about what actually went wrong. Reconciling two
    /// worlds is the CSV diff import in Sprint 7, and it is a different operation with a preview.
    /// </summary>
    private async Task GuardAgainstOverwritingAnExistingWorldAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GeoNode> existing = await _unitOfWork.GeoNodes.ListAsync(cancellationToken);
        if (existing.Count == 0)
            return;

        throw new WorldImportException(
        [
            $"database: a world is already loaded ({existing.Count} geo nodes). Import writes a world " +
            "into an empty database; it does not merge. Point at a new database file, or clear this " +
            "one first.",
        ]);
    }

    /// <summary>
    /// Roots first, then each level of children. The source file happens to be in this order
    /// already, but relying on that would make the importer fail on a hand-edited file for a
    /// reason that has nothing to do with the data being wrong.
    /// </summary>
    private static IEnumerable<GeoNode> OrderByHierarchy(IReadOnlyList<GeoNode> nodes)
    {
        var remaining = nodes.ToList();
        var placed = new HashSet<string>();

        while (remaining.Count > 0)
        {
            List<GeoNode> ready = remaining
                .Where(node => node.ParentId is null || placed.Contains(node.ParentId))
                .ToList();

            if (ready.Count == 0)
            {
                // Every remaining node points at a parent that isn't in the document (or at a
                // cycle). Naming them is the whole value of the message.
                string orphans = string.Join(", ", remaining.Select(node => $"{node.GeoNodeId} -> {node.ParentId}"));
                throw new WorldImportException([$"geoNodes: unresolved or cyclic parent references ({orphans})"]);
            }

            foreach (GeoNode node in ready)
            {
                placed.Add(node.GeoNodeId);
                remaining.Remove(node);
                yield return node;
            }
        }
    }
}
