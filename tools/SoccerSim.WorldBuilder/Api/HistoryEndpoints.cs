using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Import;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>What the undo button shows: how deep the stack is and what the next press would
/// take back.</summary>
public sealed record HistoryDto(int Depth, string? NextLabel, int Cap);

public sealed record UndoResultDto(string Undone, int Depth, int PendingEdits);

public sealed record DeleteClubResultDto(
    string ClubName,
    int Players,
    int RivalsCleared,
    int CompetitionsLeft,
    int Depth,
    int PendingEdits);

/// <summary>
/// Undo, and the deletions it exists for (ROADMAP.md Sprint 9). Deleting a club is the most
/// destructive single act the tool offers, and until there was an undo stack it was the one act
/// it deliberately did not offer.
/// </summary>
internal static class HistoryEndpoints
{
    public static void MapHistoryApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/history", GetHistoryAsync);
        api.MapPost("/undo", UndoAsync);
        api.MapDelete("/clubs/{clubId}", DeleteClubAsync);
        api.MapDelete("/characters/{playerId}", DeletePlayerAsync);
    }

    private static async Task<IResult> GetHistoryAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        WorldHistoryEntry? next = await unitOfWork.History.PeekAsync(cancellationToken);

        return Results.Ok(new HistoryDto(
            await unitOfWork.History.CountAsync(cancellationToken),
            next?.Label,
            WorldHistory.Depth));
    }

    private static async Task<IResult> UndoAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        string? undone = await WorldHistory.UndoAsync(unitOfWork, cancellationToken);

        if (undone is null)
            return Results.BadRequest(new { error = "Não há nada para desfazer." });

        return Results.Ok(new UndoResultDto(
            undone,
            await unitOfWork.History.CountAsync(cancellationToken),
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    private static async Task<IResult> DeleteClubAsync(
        string clubId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        WorldDeletions.ClubRemoval removal;
        try
        {
            removal = WorldDeletions.RemoveClub(world, clubId);
        }
        catch (ArgumentException)
        {
            return Results.NotFound();
        }

        await WorldHistory.RecordAsync(unitOfWork, $"Apagar {removal.ClubName}", cancellationToken);
        await WorldStore.ReplaceAsync(unitOfWork, removal.World, cancellationToken);

        await RecordEditAsync(
            unitOfWork,
            WorldEntityType.Club,
            clubId,
            "club",
            $"{removal.ClubName} com {removal.Players} jogadores",
            null,
            cancellationToken);

        return Results.Ok(new DeleteClubResultDto(
            removal.ClubName,
            removal.Players,
            removal.RivalsCleared,
            removal.CompetitionsLeft,
            await unitOfWork.History.CountAsync(cancellationToken),
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    private static async Task<IResult> DeletePlayerAsync(
        string playerId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        CharacterRecord? player = await unitOfWork.Characters.GetAsync(playerId, cancellationToken);
        if (player is null)
            return Results.NotFound();

        await WorldHistory.RecordAsync(unitOfWork, $"Apagar {player.FirstName} {player.LastName}", cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Characters.DeleteAsync(playerId, cancellationToken);
            await unitOfWork.Edits.RecordAsync(
                new WorldEdit(WorldEntityType.Character, playerId, "player",
                    $"{player.FirstName} {player.LastName}", null, DateTime.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Results.Ok(new UndoResultDto(
            $"{player.FirstName} {player.LastName}",
            await unitOfWork.History.CountAsync(cancellationToken),
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    private static async Task RecordEditAsync(
        IWorldUnitOfWork unitOfWork,
        WorldEntityType type,
        string entityId,
        string field,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Edits.RecordAsync(
                new WorldEdit(type, entityId, field, before, after, DateTime.UtcNow), cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
