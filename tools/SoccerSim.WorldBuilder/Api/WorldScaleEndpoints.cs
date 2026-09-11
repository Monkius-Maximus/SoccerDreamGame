using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Validation;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>One node of the drawn tree: depth, how many clubs sit on it, and whether it can go.</summary>
public sealed record GeoTreeNodeDto(
    GeoNode Node,
    int Depth,
    int Children,
    int Clubs,
    bool CanHaveChildren,
    string? BlockedReason);

public sealed record GeoTreeDto(IReadOnlyList<GeoTreeNodeDto> Nodes);

public sealed record GeoEditRequest(string? ParentId, string? DisplayName, string? ChildId);

public sealed record CountryDto(CountryProfile Country, string? DisplayName, int Clubs, LeaguePyramid Pyramid, IReadOnlyList<Finding> Findings);

public sealed record RecalculateResultDto(int Rewritten, int PendingEdits);

/// <summary>
/// The screens that only start mattering at a second league (ROADMAP.md Sprint 9): the geography
/// tree, the calibration, and the country pyramids.
/// </summary>
internal static class WorldScaleEndpoints
{
    public static void MapScaleApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/geo/tree", GetTreeAsync);
        api.MapPost("/geo/{nodeId}/rename", RenameAsync);
        api.MapPost("/geo/{nodeId}/move", MoveAsync);
        api.MapPost("/geo/{nodeId}/children", AddChildAsync);
        api.MapDelete("/geo/{nodeId}", DeleteNodeAsync);

        api.MapGet("/calibration", GetCalibrationAsync);
        api.MapPost("/calibration/recalculate", RecalculateAsync);

        api.MapGet("/countries", GetCountriesAsync);
    }

    // -------------------------------------------------------------- geography

    private static async Task<IResult> GetTreeAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        return Results.Ok(new GeoTreeDto(BuildTree(world.GeoNodes, world.Clubs)));
    }

    /// <summary>
    /// Depth-first from the roots, which is the order a tree is read in. Each node carries why it
    /// cannot be deleted, computed here rather than guessed at by the screen — the reason is the
    /// useful part, and it is the same reason the operation itself would give.
    /// </summary>
    private static IReadOnlyList<GeoTreeNodeDto> BuildTree(
        IReadOnlyList<GeoNode> nodes,
        IReadOnlyList<ClubIdentity> clubs)
    {
        ILookup<string?, GeoNode> children = nodes.ToLookup(node => node.ParentId);
        ILookup<string, ClubIdentity> byNode = clubs.ToLookup(club => club.Geography.GeoNodeId);

        var drawn = new List<GeoTreeNodeDto>();

        void Walk(GeoNode node, int depth)
        {
            int childCount = children[node.GeoNodeId].Count();
            int clubCount = byNode[node.GeoNodeId].Count();

            drawn.Add(new GeoTreeNodeDto(
                node,
                depth,
                childCount,
                clubCount,
                GeoTree.CanHaveChildren(node.Kind),
                childCount > 0 ? $"{childCount} nó(s) abaixo"
                    : clubCount > 0 ? $"{clubCount} clube(s) aqui"
                    : null));

            foreach (GeoNode child in children[node.GeoNodeId].OrderBy(c => c.DisplayName, StringComparer.Ordinal))
                Walk(child, depth + 1);
        }

        foreach (GeoNode root in children[null].OrderBy(node => node.DisplayName, StringComparer.Ordinal))
            Walk(root, 0);

        return drawn;
    }

    private static Task<IResult> RenameAsync(
        string nodeId,
        GeoEditRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditTreeAsync(unitOfWork, cancellationToken,
            (nodes, _) => GeoTree.Rename(nodes, nodeId, request.DisplayName ?? string.Empty));

    private static Task<IResult> MoveAsync(
        string nodeId,
        GeoEditRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditTreeAsync(unitOfWork, cancellationToken,
            (nodes, _) => GeoTree.Move(nodes, nodeId, request.ParentId ?? string.Empty));

    private static Task<IResult> AddChildAsync(
        string nodeId,
        GeoEditRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditTreeAsync(unitOfWork, cancellationToken,
            (nodes, _) => GeoTree.AddChild(
                nodes, nodeId, request.ChildId ?? string.Empty, request.DisplayName ?? string.Empty));

    private static Task<IResult> DeleteNodeAsync(
        string nodeId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditTreeAsync(unitOfWork, cancellationToken, (nodes, clubs) => GeoTree.Delete(nodes, clubs, nodeId));

    /// <summary>
    /// Applies a tree edit and writes the whole world back. Coarse on purpose: a move reparents
    /// one row but changes the shape every other screen reads, and the rules that make it safe
    /// are stated over the whole set.
    /// </summary>
    private static async Task<IResult> EditTreeAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<GeoNode>, IReadOnlyList<ClubIdentity>, IReadOnlyList<GeoNode>> edit)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        IReadOnlyList<GeoNode> nodes;
        try
        {
            nodes = edit(world.GeoNodes, world.Clubs);
        }
        catch (GeoTreeException ex)
        {
            // A refused edit is an operational answer with a reason, not a server fault.
            return Results.BadRequest(new { error = ex.Message });
        }

        await WorldHistory.RecordAsync(unitOfWork, "Editar geografia", cancellationToken);
        await WorldStore.ReplaceAsync(unitOfWork, world with { GeoNodes = nodes }, cancellationToken);

        return Results.Ok(new GeoTreeDto(BuildTree(nodes, world.Clubs)));
    }

    // ------------------------------------------------------------ calibration

    private static async Task<IResult> GetCalibrationAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        return Results.Ok(CalibrationReviewer.Review(world));
    }

    /// <summary>
    /// Rewrites every player's economy from the calibration as it stands. One act, because a
    /// re-fit constant ages all 688 at once and correcting them one edit at a time is not
    /// something a person does.
    /// </summary>
    private static async Task<IResult> RecalculateAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        int stale = CalibrationReviewer.Review(world).StalePlayers;
        IReadOnlyList<CharacterRecord> recalculated = CalibrationReviewer.Recalculate(world);

        await WorldHistory.RecordAsync(
            unitOfWork, $"Recalcular o lote ({stale} divergentes)", cancellationToken);
        await WorldStore.ReplaceAsync(unitOfWork, world with { Characters = recalculated }, cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Edits.RecordAsync(
                new WorldEdit(WorldEntityType.Club, "world", "calibration.recalculate",
                    $"{stale} divergentes", "0 divergentes", DateTime.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Results.Ok(new RecalculateResultDto(stale, await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    // -------------------------------------------------------------- countries

    private static async Task<IResult> GetCountriesAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        IReadOnlyList<CountryProfile> countries = await unitOfWork.Countries.ListAsync(cancellationToken);

        Dictionary<string, string> names = CountryNames(world);
        ILookup<string, ClubIdentity> byCountry = world.Clubs.ToLookup(club => club.Geography.CountryId);

        var result = new List<CountryDto>();

        foreach (CountryProfile country in countries)
        {
            LeaguePyramid pyramid = await unitOfWork.Divisions.GetPyramidAsync(country.CountryId, cancellationToken);

            result.Add(new CountryDto(
                country,
                names.GetValueOrDefault(country.CountryId),
                byCountry[country.CountryId].Count(),
                pyramid,
                // A country with no divisions yet is not broken, it is unfinished — so the rules
                // only speak once there is a pyramid to speak about.
                pyramid.Divisions.Count == 0 ? [] : PyramidRules.Check(pyramid)));
        }

        return Results.Ok(result);
    }

    /// <summary>The ISO code and the geo node are different identifiers for the same place, and
    /// nothing links them directly; a club knows both, so the link goes through the clubs.</summary>
    private static Dictionary<string, string> CountryNames(WorldSnapshot world)
    {
        Dictionary<string, GeoNode> nodes = world.GeoNodes.ToDictionary(node => node.GeoNodeId);
        var names = new Dictionary<string, string>();

        foreach (ClubIdentity club in world.Clubs)
        {
            if (names.ContainsKey(club.Geography.CountryId))
                continue;

            GeoNode? node = nodes.GetValueOrDefault(club.Geography.GeoNodeId);
            while (node is not null && node.Kind != GeoNodeKind.Country)
                node = node.ParentId is null ? null : nodes.GetValueOrDefault(node.ParentId);

            if (node is not null)
                names[club.Geography.CountryId] = node.DisplayName;
        }

        return names;
    }
}
