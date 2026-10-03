using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Squad;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>What the "Gerar divisão" dialog sends. The country and the division are the route's.</summary>
public sealed record DivisionGenerationBody(
    int ClubCount,
    PrestigeBand Band,
    double StrengthMin,
    double StrengthMax,
    long Seed);

/// <summary>One row of the preview table: the club, where it is, how strong it was asked to be,
/// and the overall of the eleven its generated squad would field.</summary>
public sealed record DivisionPreviewClubDto(
    string ClubId,
    string ShortName,
    string OfficialName,
    string CityName,
    string Uf,
    PrestigeBand Band,
    double Strength,
    int Players,
    int XiOverall);

/// <summary>A division the user can look at before deciding. Nothing is written to produce it;
/// the same request produces the same batch on apply, as long as the world has not changed.</summary>
public sealed record DivisionGenerationPreviewDto(
    IReadOnlyList<DivisionPreviewClubDto> Clubs,
    int Players,
    DivisionGenerationBody Request);

public sealed record DivisionGenerationResultDto(int Clubs, int Players, int Depth, int PendingEdits);

/// <summary>
/// A whole division from nothing (ADR-0011 §6, Sprint 11b). Thin on purpose: loading, generating
/// and writing are <see cref="DivisionCreation"/>'s, the same path the <c>generate-division</c>
/// verb takes. The dialog's bands and fresh seed come from <c>/api/clubs/generate/options</c>.
/// </summary>
internal static class DivisionGenerationEndpoints
{
    public static void MapDivisionGenerationApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapPost("/countries/{countryId}/divisions/{divisionId}/generate/preview", PreviewAsync);
        api.MapPost("/countries/{countryId}/divisions/{divisionId}/generate/apply", ApplyAsync);
    }

    private static async Task<IResult> PreviewAsync(
        string countryId,
        string divisionId,
        DivisionGenerationBody body,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        DivisionGenerationResult batch;
        try
        {
            batch = await DivisionCreation.PreviewAsync(unitOfWork, ToRequest(countryId, divisionId, body), cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A request the pyramid cannot hold, or a world without what generation needs: both
            // have a clear remedy.
            return Results.BadRequest(new { error = ex.Message });
        }

        ILookup<string, CharacterRecord> squads = batch.Characters.ToLookup(player => player.ClubId);

        return Results.Ok(new DivisionGenerationPreviewDto(
            batch.Clubs.Select(club => new DivisionPreviewClubDto(
                club.ClubId,
                club.Identity.ShortName,
                club.Identity.OfficialName,
                club.Geography.CityName,
                club.Geography.Uf,
                club.World.PrestigeBand,
                club.World.ClubStrength,
                squads[club.ClubId].Count(),
                ProbableEleven.For(club, [.. squads[club.ClubId]]).Overall)).ToList(),
            batch.Characters.Count,
            body));
    }

    private static async Task<IResult> ApplyAsync(
        string countryId,
        string divisionId,
        DivisionGenerationBody body,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        DivisionGenerationResult batch;
        try
        {
            batch = await DivisionCreation.ApplyAsync(unitOfWork, ToRequest(countryId, divisionId, body), cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        return Results.Ok(new DivisionGenerationResultDto(
            batch.Clubs.Count,
            batch.Characters.Count,
            await unitOfWork.History.CountAsync(cancellationToken),
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    private static DivisionGenerationRequest ToRequest(string countryId, string divisionId, DivisionGenerationBody body) =>
        new(countryId, divisionId, body.ClubCount, body.Band, body.StrengthMin, body.StrengthMax, body.Seed);
}
