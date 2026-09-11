using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Fields;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>
/// The write surface (Sprint 4). Field-path patching, one field at a time, validated on the
/// server against the same catalog the form is built from.
///
/// <para>
/// Three rules hold here. The client is never trusted: an enum arrives as a string and is checked
/// against the closed set, and a bad value returns 400 having written nothing. Derived values are
/// never accepted: they are recomputed on write, and patching one tells you which field actually
/// decides it. And a write carries the version it read: if the row moved on, the answer is 409
/// and the caller re-reads, because silently overwriting someone else's edit is the one failure
/// a tool that is the source of truth cannot have.
/// </para>
/// </summary>
internal static class EditEndpoints
{
    public static void MapEditApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/fields", GetFields);
        api.MapGet("/pending", GetPendingAsync);
        api.MapPatch("/clubs/{clubId}", PatchClubAsync);
        api.MapGet("/characters/{playerId}", GetCharacterAsync);
        api.MapPatch("/characters/{playerId}", PatchCharacterAsync);
        api.MapPost("/characters/{playerId}/recalculate", RecalculateCharacterAsync);
    }

    /// <summary>The form definition. The UI renders its groups from this, so a field added to the
    /// catalog appears on the page and is accepted by the server in the same change.</summary>
    private static FieldCatalogDto GetFields() => new(ClubFields.Groups, CharacterFields.Groups);

    private static async Task<int> GetPendingAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        await unitOfWork.Edits.CountPendingAsync(cancellationToken);

    private static async Task<IResult> PatchClubAsync(
        string clubId,
        FieldPatchRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ClubIdentity? club = await unitOfWork.Clubs.GetAsync(clubId, cancellationToken);
        if (club is null)
            return Results.NotFound();

        ClubIdentity updated;
        string? before;
        try
        {
            before = ClubFields.Read(club, request.Path);
            updated = ClubFields.Apply(club, request.Path, request.Value);
        }
        catch (FieldPatchException ex)
        {
            return Results.BadRequest(new { error = ex.Message, path = ex.Path });
        }

        return await WriteAsync(
            unitOfWork,
            cancellationToken,
            write: () => unitOfWork.Clubs.UpdateAsync(updated, request.Version, cancellationToken),
            edit: new WorldEdit(
                WorldEntityType.Club,
                clubId,
                request.Path,
                before,
                ClubFields.Read(updated, request.Path),
                DateTime.UtcNow));
    }

    private static async Task<IResult> GetCharacterAsync(
        string playerId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        CharacterRecord? character = await unitOfWork.Characters.GetAsync(playerId, cancellationToken);
        if (character is null)
            return Results.NotFound();

        WorldCalibration calibration = await RequireCalibrationAsync(unitOfWork, cancellationToken);
        ClubIdentity? club = await unitOfWork.Clubs.GetAsync(character.ClubId, cancellationToken);
        if (club is null)
            return Results.NotFound();

        CharacterRecord recalculated = WorldDerivations.Recalculate(character, club.World.PrestigeBand, calibration);

        var stored = new CharacterEconomyDto(
            character.Overall, character.PotentialOverall, character.MarketValueEur, character.SalaryMonthlyBrl);
        var fresh = new CharacterEconomyDto(
            recalculated.Overall, recalculated.PotentialOverall, recalculated.MarketValueEur, recalculated.SalaryMonthlyBrl);

        return Results.Ok(new CharacterPageDto(
            Character: character,
            Version: await unitOfWork.Characters.GetVersionAsync(playerId, cancellationToken),
            PositionWeights: calibration.PositionWeights[character.PrimaryPosition],
            Stored: stored,
            Recalculated: fresh,
            EconomyDiverges: stored != fresh));
    }

    private static async Task<IResult> PatchCharacterAsync(
        string playerId,
        FieldPatchRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        CharacterRecord? character = await unitOfWork.Characters.GetAsync(playerId, cancellationToken);
        if (character is null)
            return Results.NotFound();

        CharacterRecord updated;
        string? before;
        try
        {
            before = CharacterFields.Read(character, request.Path);
            updated = CharacterFields.Apply(character, request.Path, request.Value);
        }
        catch (FieldPatchException ex)
        {
            return Results.BadRequest(new { error = ex.Message, path = ex.Path });
        }

        return await WriteAsync(
            unitOfWork,
            cancellationToken,
            write: () => unitOfWork.Characters.UpdateAsync(updated, request.Version, cancellationToken),
            edit: new WorldEdit(
                WorldEntityType.Character,
                playerId,
                request.Path,
                before,
                CharacterFields.Read(updated, request.Path),
                DateTime.UtcNow));
    }

    /// <summary>
    /// Rewrites the character unchanged, which recalculates the economy against today's
    /// calibration. This is the modal's "recalculate" button: the divergence is shown first, and
    /// applying it is an explicit act.
    /// </summary>
    private static async Task<IResult> RecalculateCharacterAsync(
        string playerId,
        FieldPatchRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        CharacterRecord? character = await unitOfWork.Characters.GetAsync(playerId, cancellationToken);
        if (character is null)
            return Results.NotFound();

        return await WriteAsync(
            unitOfWork,
            cancellationToken,
            write: () => unitOfWork.Characters.UpdateAsync(character, request.Version, cancellationToken),
            edit: new WorldEdit(
                WorldEntityType.Character,
                playerId,
                "economy",
                $"OVR {character.Overall} · {character.MarketValueEur} EUR",
                "recalculado",
                DateTime.UtcNow));
    }

    /// <summary>
    /// The write half every patch shares: one transaction covering both the row and its edit-log
    /// entry, so the pending counter can never disagree with what is actually stored.
    /// </summary>
    /// <summary>
    /// The single funnel every field patch goes through — which is why the undo snapshot is taken
    /// here rather than in each handler: a new patch endpoint gets undo by existing, not by
    /// remembering. The snapshot is pushed INSIDE the transaction, so a write that loses a
    /// concurrency race rolls the undo entry back with it instead of leaving a step that undoes
    /// nothing.
    /// </summary>
    private static async Task<IResult> WriteAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken,
        Func<Task<long>> write,
        WorldEdit edit)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await WorldHistory.RecordAsync(unitOfWork, $"Editar {edit.FieldPath}", cancellationToken);

            long version = await write();
            await unitOfWork.Edits.RecordAsync(edit, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return Results.Ok(new FieldPatchResponse(version, await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
        }
        catch (WorldConcurrencyException ex)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return Results.Conflict(new
            {
                error = ex.Message,
                expectedVersion = ex.ExpectedVersion,
                actualVersion = ex.ActualVersion,
            });
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<WorldCalibration> RequireCalibrationAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        await unitOfWork.Calibration.GetAsync(cancellationToken)
        ?? throw new InvalidOperationException("No calibration is loaded; run `worldbuilder import <file>` first.");
}
