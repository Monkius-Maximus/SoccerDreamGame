using SoccerSim.Core.Persistence;

namespace SoccerSim.Core.World.Import;

/// <summary>
/// Reads the whole world out of the database and writes a whole world back in — the two
/// operations export and CSV import need, and the symmetric pair to <see cref="WorldImporter"/>,
/// which only ever writes into an empty database.
/// </summary>
public static class WorldStore
{
    public static async Task<WorldSnapshot> LoadAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        WorldCalibration calibration = await unitOfWork.Calibration.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No calibration is loaded; run `worldbuilder import <file>` first.");

        string? seed = await unitOfWork.Settings.GetAsync(WorldMeta.MasterSeedKey, cancellationToken);
        string? schema = await unitOfWork.Settings.GetAsync(WorldMeta.SchemaVersionKey, cancellationToken);

        return new WorldSnapshot(
            await unitOfWork.GeoNodes.ListAsync(cancellationToken),
            calibration,
            await unitOfWork.Clubs.ListAsync(cancellationToken),
            await unitOfWork.Characters.ListAsync(cancellationToken),
            await unitOfWork.Competitions.ListAsync(cancellationToken),
            await unitOfWork.Sources.ListAsync(cancellationToken),
            new WorldMeta(
                seed is null ? 0 : long.Parse(seed),
                schema ?? "unknown",
                await unitOfWork.Settings.GetAsync(WorldMeta.SourceFileKey, cancellationToken)));
    }

    /// <summary>
    /// Replaces the stored world with this one, in a single transaction. Deletes first and in
    /// foreign-key order: characters point at clubs, clubs at geo nodes, so the world comes apart
    /// in the reverse of the order it goes together.
    ///
    /// <para>Destructive by design and only ever called behind a preview — this is what applying
    /// a CSV import does, and the user has already seen exactly which rows enter and leave.</para>
    /// </summary>
    public static async Task ReplaceAsync(
        IWorldUnitOfWork unitOfWork,
        WorldSnapshot world,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CharacterRecord> characters = await unitOfWork.Characters.ListAsync(cancellationToken);
        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync(cancellationToken);
        IReadOnlyList<Competition> competitions = await unitOfWork.Competitions.ListAsync(cancellationToken);
        IReadOnlyList<GeoNode> geoNodes = await unitOfWork.GeoNodes.ListAsync(cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (CharacterRecord character in characters)
                await unitOfWork.Characters.DeleteAsync(character.PlayerId, cancellationToken);

            foreach (Competition competition in competitions)
                await unitOfWork.Competitions.DeleteAsync(competition.CompetitionId, cancellationToken);

            foreach (ClubIdentity club in clubs)
                await unitOfWork.Clubs.DeleteAsync(club.ClubId, cancellationToken);

            // Children before parents: a node cannot go while something still hangs off it.
            foreach (GeoNode node in geoNodes.Reverse())
                await unitOfWork.GeoNodes.DeleteAsync(node.GeoNodeId, cancellationToken);

            foreach (GeoNode node in OrderByHierarchy(world.GeoNodes))
                await unitOfWork.GeoNodes.AddAsync(node, cancellationToken);

            await unitOfWork.Calibration.SaveAsync(world.Calibration, cancellationToken);

            foreach (ClubIdentity club in world.Clubs)
                await unitOfWork.Clubs.AddAsync(club, cancellationToken);

            foreach (CharacterRecord character in world.Characters)
                await unitOfWork.Characters.AddAsync(character, cancellationToken);

            foreach (Competition competition in world.Competitions)
                await unitOfWork.Competitions.AddAsync(competition, cancellationToken);

            await unitOfWork.Sources.ReplaceAllAsync(world.Sources, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>Roots first, then each level of children, so a node's parent always exists before
    /// it is inserted. Same rule the importer uses, for the same reason.</summary>
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
                string orphans = string.Join(", ", remaining.Select(node => $"{node.GeoNodeId} -> {node.ParentId}"));
                throw new Serialization.WorldImportException(
                    [$"GeoNodes: unresolved or cyclic parent references ({orphans})"]);
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
