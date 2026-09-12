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

/// <summary>A club of this country and the division it plays in, or null when it plays in none.
/// One list rather than two: the chips need names for the enrolled and the select needs the rest,
/// and a screen that received them separately could show a club in both.</summary>
public sealed record CountryClubDto(string ClubId, string ShortName, string? DivisionId);

public sealed record CountryDto(
    CountryProfile Country,
    string? DisplayName,
    int Clubs,
    LeaguePyramid Pyramid,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<CountryClubDto> Roster);

public sealed record RecalculateResultDto(int Rewritten, int PendingEdits);

/// <summary>A new country. The nationality mix is not here: a country is created before anyone
/// knows where its players come from, and the sweep already reports a missing mix as an error.</summary>
public sealed record NewCountryRequest(string? CountryId, string? Currency, double EurToLocal, int WageFloorMonthly);

public sealed record DivisionRequest(
    string? DivisionId,
    string? Name,
    CompetitionFormat Format,
    int ClubCount,
    int PromotedIn,
    int RelegatedOut);

public sealed record EnrolRequest(string? ClubId);

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
        api.MapPost("/countries", AddCountryAsync);
        api.MapDelete("/countries/{countryId}", DeleteCountryAsync);

        api.MapPost("/countries/{countryId}/divisions", AddDivisionAsync);
        api.MapPut("/countries/{countryId}/divisions/{divisionId}", RewriteDivisionAsync);
        api.MapDelete("/countries/{countryId}/divisions/{divisionId}", DeleteDivisionAsync);
        api.MapPost("/countries/{countryId}/divisions/{divisionId}/clubs", EnrolAsync);
        api.MapDelete("/countries/{countryId}/divisions/{divisionId}/clubs/{clubId}", WithdrawAsync);
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

    private static async Task<IResult> GetCountriesAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        Results.Ok(await BuildCountriesAsync(unitOfWork, cancellationToken));

    /// <summary>
    /// Creates a country. Nothing but the money: the nationality mix is stated later, and the
    /// pyramid is built division by division — a country that exists with neither is not broken,
    /// it is the first step.
    /// </summary>
    private static async Task<IResult> AddCountryAsync(
        NewCountryRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        string countryId = (request.CountryId ?? string.Empty).Trim().ToUpperInvariant();

        if (countryId.Length != 3)
            return Results.BadRequest(new { error = "o código do país tem três letras, como 'ARG'." });

        if (string.IsNullOrWhiteSpace(request.Currency))
            return Results.BadRequest(new { error = "um país precisa de uma moeda." });

        if (request.EurToLocal <= 0)
            return Results.BadRequest(new { error = "a taxa do euro precisa ser maior que zero." });

        if (request.WageFloorMonthly < 0)
            return Results.BadRequest(new { error = "o piso salarial não pode ser negativo." });

        if (await unitOfWork.Countries.GetAsync(countryId, cancellationToken) is not null)
            return Results.BadRequest(new { error = $"{countryId} já existe." });

        await WorldHistory.RecordAsync(unitOfWork, $"Criar o país {countryId}", cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Countries.SaveAsync(
                new CountryProfile(
                    countryId,
                    request.Currency.Trim().ToUpperInvariant(),
                    request.EurToLocal,
                    request.WageFloorMonthly,
                    // Empty rather than invented: the generator refuses to populate a country with
                    // no mix, which is the correct answer until someone states one.
                    NationalityMix: [],
                    NationalityMixSource: null),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Results.Ok(await BuildCountriesAsync(unitOfWork, cancellationToken));
    }

    /// <summary>
    /// Deletes a country. Refused while clubs still name it or divisions still hang off it — the
    /// same rule the geography tree uses, for the same reason: the rows would not disappear with
    /// it, they would simply stop pointing at anything.
    /// </summary>
    private static async Task<IResult> DeleteCountryAsync(
        string countryId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (await unitOfWork.Countries.GetAsync(countryId, cancellationToken) is null)
            return Results.NotFound(new { error = $"não existe o país '{countryId}'." });

        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        int clubs = world.Clubs.Count(club => club.Geography.CountryId == countryId);

        if (clubs > 0)
            return Results.BadRequest(new { error = $"{countryId} tem {clubs} clube(s) — mova-os antes de apagá-lo." });

        LeaguePyramid pyramid = await unitOfWork.Divisions.GetPyramidAsync(countryId, cancellationToken);
        if (pyramid.Divisions.Count > 0)
            return Results.BadRequest(new { error = $"{countryId} tem {pyramid.Divisions.Count} divisão(ões) — apague-as antes." });

        await WorldHistory.RecordAsync(unitOfWork, $"Apagar o país {countryId}", cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Countries.DeleteAsync(countryId, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Results.Ok(await BuildCountriesAsync(unitOfWork, cancellationToken));
    }

    // -------------------------------------------------------------- divisions

    private static Task<IResult> AddDivisionAsync(
        string countryId,
        DivisionRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditPyramidAsync(unitOfWork, countryId, $"Criar a divisão {request.Name}", cancellationToken,
            (pyramid, _) => PyramidEditor.AddDivision(
                pyramid, request.DivisionId ?? string.Empty, request.Name ?? string.Empty,
                request.Format, request.ClubCount));

    private static Task<IResult> RewriteDivisionAsync(
        string countryId,
        string divisionId,
        DivisionRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditPyramidAsync(unitOfWork, countryId, $"Editar a divisão {request.Name}", cancellationToken,
            (pyramid, _) => PyramidEditor.Rewrite(
                pyramid, divisionId, request.Name ?? string.Empty, request.Format,
                request.ClubCount, request.PromotedIn, request.RelegatedOut));

    private static Task<IResult> DeleteDivisionAsync(
        string countryId,
        string divisionId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditPyramidAsync(unitOfWork, countryId, $"Apagar a divisão {divisionId}", cancellationToken,
            (pyramid, _) => PyramidEditor.RemoveDivision(pyramid, divisionId));

    private static Task<IResult> EnrolAsync(
        string countryId,
        string divisionId,
        EnrolRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditPyramidAsync(unitOfWork, countryId, $"Inscrever {request.ClubId} em {divisionId}", cancellationToken,
            (pyramid, world) =>
            {
                ClubIdentity club = world.Clubs.FirstOrDefault(c => c.ClubId == request.ClubId)
                    ?? throw new PyramidException($"não existe o clube '{request.ClubId}'.");

                // A division is national. A club from elsewhere in it is not a bigger league, it is
                // a club that plays two national seasons.
                if (club.Geography.CountryId != countryId)
                {
                    throw new PyramidException(
                        $"{club.Identity.OfficialName} é de {club.Geography.CountryId}, não de {countryId}.");
                }

                return PyramidEditor.Enrol(pyramid, divisionId, club.ClubId);
            });

    private static Task<IResult> WithdrawAsync(
        string countryId,
        string divisionId,
        string clubId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        EditPyramidAsync(unitOfWork, countryId, $"Retirar {clubId} de {divisionId}", cancellationToken,
            (pyramid, _) => PyramidEditor.Withdraw(pyramid, divisionId, clubId));

    /// <summary>
    /// Applies a pyramid edit and writes the country back. Like the geography tree, the answer is
    /// the whole list rather than the one row that changed: adding a division renumbers the tiers
    /// below it and enrolling a club empties a slot somewhere else, so a screen that patched one
    /// row would be showing a pyramid that no longer exists.
    /// </summary>
    private static async Task<IResult> EditPyramidAsync(
        IWorldUnitOfWork unitOfWork,
        string countryId,
        string label,
        CancellationToken cancellationToken,
        Func<LeaguePyramid, WorldSnapshot, LeaguePyramid> edit)
    {
        if (await unitOfWork.Countries.GetAsync(countryId, cancellationToken) is null)
            return Results.NotFound(new { error = $"não existe o país '{countryId}'." });

        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        LeaguePyramid pyramid = await unitOfWork.Divisions.GetPyramidAsync(countryId, cancellationToken);

        LeaguePyramid edited;
        try
        {
            edited = edit(pyramid, world);
        }
        catch (PyramidException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        await WorldHistory.RecordAsync(unitOfWork, label, cancellationToken);
        await WorldScale.SavePyramidAsync(unitOfWork, edited, cancellationToken);

        return Results.Ok(await BuildCountriesAsync(unitOfWork, cancellationToken));
    }

    private static async Task<IReadOnlyList<CountryDto>> BuildCountriesAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        IReadOnlyList<CountryProfile> countries = await unitOfWork.Countries.ListAsync(cancellationToken);

        Dictionary<string, string> names = CountryNames(world);
        ILookup<string, ClubIdentity> byCountry = world.Clubs.ToLookup(club => club.Geography.CountryId);

        var result = new List<CountryDto>();

        foreach (CountryProfile country in countries)
        {
            LeaguePyramid pyramid = await unitOfWork.Divisions.GetPyramidAsync(country.CountryId, cancellationToken);

            Dictionary<string, string> enrolledIn = pyramid.Divisions
                .SelectMany(division => division.ClubIds.Select(clubId => (clubId, division.DivisionId)))
                .ToDictionary(entry => entry.clubId, entry => entry.DivisionId);

            result.Add(new CountryDto(
                country,
                names.GetValueOrDefault(country.CountryId),
                byCountry[country.CountryId].Count(),
                pyramid,
                // A country with no divisions yet is not broken, it is unfinished — so the rules
                // only speak once there is a pyramid to speak about.
                pyramid.Divisions.Count == 0 ? [] : PyramidRules.Check(pyramid),
                [.. byCountry[country.CountryId]
                    .OrderBy(club => club.Identity.ShortName, StringComparer.Ordinal)
                    .Select(club => new CountryClubDto(
                        club.ClubId,
                        club.Identity.ShortName,
                        enrolledIn.GetValueOrDefault(club.ClubId)))]));
        }

        return result;
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
