using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Validation;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>What the "Gerar clube" dialog opens with: the countries it can generate in (those with
/// imported club profiles), the bands, and a fresh seed.</summary>
public sealed record ClubGenerationOptionsDto(
    IReadOnlyList<string> Countries,
    IReadOnlyList<PrestigeBand> Bands,
    long Seed);

/// <summary>A club the user can look at before deciding. Nothing is written to produce it; the same
/// request produces the same club again on apply, as long as the world has not changed.</summary>
public sealed record ClubGenerationPreviewDto(
    ClubIdentity Club,
    IReadOnlyList<string> GeoPath,
    IReadOnlyList<Finding> Findings,
    FindingLevel InvariantLevel,
    ClubGenerationRequest Request);

public sealed record ClubGenerationResultDto(string ClubId, string OfficialName, int Depth, int PendingEdits);

/// <summary>
/// A club from nothing (ADR-0011, Sprint 10b). Thin on purpose: loading, generating and writing
/// are <see cref="ClubCreation"/>'s, the same path the <c>generate-club</c> verb takes.
/// </summary>
internal static class ClubGenerationEndpoints
{
    public static void MapClubGenerationApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/clubs/generate/options", GetOptionsAsync);
        api.MapPost("/clubs/generate/preview", PreviewAsync);
        api.MapPost("/clubs/generate/apply", ApplyAsync);
    }

    private static async Task<IResult> GetOptionsAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        Results.Ok(new ClubGenerationOptionsDto(
            await unitOfWork.ClubProfiles.ListCountriesAsync(cancellationToken),
            Enum.GetValues<PrestigeBand>(),
            // A fresh seed each time the dialog opens, so "generate again" means something without
            // the user having to invent a number.
            Random.Shared.NextInt64(1, 1_000_000)));

    private static async Task<IResult> PreviewAsync(
        ClubGenerationRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ClubIdentity club;
        try
        {
            club = await ClubCreation.PreviewAsync(unitOfWork, request, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A bad request, or a world without what generation needs: both have a clear remedy.
            return Results.BadRequest(new { error = ex.Message });
        }

        WorldCalibration calibration = await unitOfWork.Calibration.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("No calibration is loaded; run `worldbuilder import <file>` first.");
        IReadOnlyList<Finding> findings = ClubInvariants.Check(club, calibration);

        return Results.Ok(new ClubGenerationPreviewDto(
            club,
            await WorldEndpoints.BuildGeoPathAsync(club.Geography.GeoNodeId, unitOfWork, cancellationToken),
            findings,
            ClubInvariants.WorstLevel(findings),
            request));
    }

    private static async Task<IResult> ApplyAsync(
        ClubGenerationRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ClubIdentity club;
        try
        {
            club = await ClubCreation.ApplyAsync(unitOfWork, request, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        return Results.Ok(new ClubGenerationResultDto(
            club.ClubId,
            club.Identity.OfficialName,
            await unitOfWork.History.CountAsync(cancellationToken),
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }
}
