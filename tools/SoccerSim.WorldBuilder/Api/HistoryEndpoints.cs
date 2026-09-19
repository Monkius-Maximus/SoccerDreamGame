using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Import;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>What the undo button shows: how deep the stack is and what the next press would
/// take back.</summary>
public sealed record HistoryDto(int Depth, string? NextLabel, int Cap);

public sealed record UndoResultDto(string Undone, int Depth, int PendingEdits);

/// <summary>
/// One act on the stack, as the timeline draws it. <see cref="StepsBack"/> is how many acts
/// returning here would discard — the number the screen puts on the button, because "go back to
/// here" and "throw four things away" are the same click and only one of them is obvious.
/// </summary>
public sealed record HistoryStepDto(long Id, string Label, DateTime TakenAt, int StepsBack, int Edits);

/// <summary>
/// One recorded field change, with the names resolved. <see cref="EntityName"/> is null when the
/// entity no longer exists — the trail outlives what it describes, which is the point of keeping
/// it — and the screen then shows the id it still has.
/// </summary>
public sealed record EditEntryDto(
    long Id,
    string EntityType,
    string EntityId,
    string? EntityName,
    string? ClubId,
    string FieldPath,
    string? OldValue,
    string? NewValue,
    DateTime EditedAt,
    long? HistoryId,
    /// <summary>The label of the act this edit belonged to, or null once that act has fallen off
    /// the capped stack.</summary>
    string? ActLabel,
    bool Exported);

public sealed record TimelineDto(
    IReadOnlyList<HistoryStepDto> Steps,
    IReadOnlyList<EditEntryDto> Edits,
    int EditTotal,
    int EditOffset,
    int PageSize,
    int Pending,
    int Cap);

public sealed record RevertResultDto(string Label, int Discarded, int Depth, int PendingEdits);

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
        api.MapGet("/history/timeline", GetTimelineAsync);
        api.MapPost("/history/revert/{entryId:long}", RevertAsync);
        api.MapPost("/undo", UndoAsync);
        api.MapDelete("/clubs/{clubId}", DeleteClubAsync);
        api.MapDelete("/characters/{playerId}", DeletePlayerAsync);
    }

    /// <summary>How many edits one page of the trail shows. Fifty because the page is read, not
    /// scanned — and because the import that writes four hundred rows at once would otherwise be
    /// the only thing on screen.</summary>
    private const int PageSize = 50;

    /// <summary>
    /// The whole screen in one response: the acts you can return to, and a page of the field-level
    /// trail. Two different things about the same past — the stack is capped at 25 and lets you
    /// travel; the trail is unbounded, append-only, and only lets you look.
    /// </summary>
    private static async Task<IResult> GetTimelineAsync(
        int? offset,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(0, offset ?? 0);

        IReadOnlyList<WorldHistoryStep> steps = await unitOfWork.History.ListAsync(cancellationToken);
        IReadOnlyList<WorldEditEntry> edits = await unitOfWork.Edits.ListAsync(PageSize, page, cancellationToken);
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        // Counted over the whole table, not over the page on screen: the number beside an act is a
        // fact about the act, and one that shrank as the reader paged away would be a lie.
        IReadOnlyDictionary<long, int> perAct = await unitOfWork.Edits.CountByActAsync(
            [.. steps.Select(step => step.Id)], cancellationToken);

        Dictionary<long, string> labels = steps.ToDictionary(step => step.Id, step => step.Label);

        Dictionary<string, string> clubNames = world.Clubs.ToDictionary(
            club => club.ClubId, club => club.Identity.ShortName);
        Dictionary<string, CharacterRecord> players = world.Characters.ToDictionary(
            player => player.PlayerId);

        return Results.Ok(new TimelineDto(
            [.. steps.Select((step, index) => new HistoryStepDto(
                step.Id,
                step.Label,
                step.TakenAt,
                // The newest act is one step back; the one below it, two.
                index + 1,
                perAct.GetValueOrDefault(step.Id)))],
            [.. edits.Select(entry => ToDto(entry, labels, clubNames, players))],
            await unitOfWork.Edits.CountAsync(cancellationToken),
            page,
            PageSize,
            await unitOfWork.Edits.CountPendingAsync(cancellationToken),
            WorldHistory.Depth));
    }

    private static EditEntryDto ToDto(
        WorldEditEntry entry,
        IReadOnlyDictionary<long, string> labels,
        IReadOnlyDictionary<string, string> clubNames,
        IReadOnlyDictionary<string, CharacterRecord> players)
    {
        WorldEdit edit = entry.Edit;

        string? name = null;
        string? clubId = null;

        if (edit.EntityType == WorldEntityType.Club)
        {
            name = clubNames.GetValueOrDefault(edit.EntityId);
            clubId = name is null ? null : edit.EntityId;
        }
        else if (players.TryGetValue(edit.EntityId, out CharacterRecord? player))
        {
            name = $"{player.FirstName} {player.LastName}";
            clubId = player.ClubId;
        }

        return new EditEntryDto(
            entry.Id,
            edit.EntityType.ToString(),
            edit.EntityId,
            name,
            clubId,
            edit.FieldPath,
            edit.OldValue,
            edit.NewValue,
            edit.EditedAt,
            edit.HistoryId,
            edit.HistoryId is { } act ? labels.GetValueOrDefault(act) : null,
            entry.ExportedAt is not null);
    }

    /// <summary>
    /// Walks the world back to a chosen act. Destructive in one direction only: the acts after it
    /// are discarded, because there is no redo and nothing can replay them. The screen says how
    /// many before the click.
    /// </summary>
    private static async Task<IResult> RevertAsync(
        long entryId,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorldHistoryStep> before = await unitOfWork.History.ListAsync(cancellationToken);
        int discarded = before.Count(step => step.Id >= entryId);

        string? label = await WorldHistory.RevertToAsync(unitOfWork, entryId, cancellationToken);

        if (label is null)
        {
            return Results.BadRequest(new
            {
                error = "Esse ponto não está mais na pilha — a pilha guarda "
                    + $"{WorldHistory.Depth} passos e esse já saiu.",
            });
        }

        return Results.Ok(new RevertResultDto(
            label,
            discarded,
            await unitOfWork.History.CountAsync(cancellationToken),
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
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

        long act = await WorldHistory.RecordAsync(unitOfWork, $"Apagar {removal.ClubName}", cancellationToken);
        await WorldStore.ReplaceAsync(unitOfWork, removal.World, cancellationToken);

        await RecordEditAsync(
            unitOfWork,
            WorldEntityType.Club,
            clubId,
            "club",
            $"{removal.ClubName} com {removal.Players} jogadores",
            null,
            act,
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

        long act = await WorldHistory.RecordAsync(
            unitOfWork, $"Apagar {player.FirstName} {player.LastName}", cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Characters.DeleteAsync(playerId, cancellationToken);
            await unitOfWork.Edits.RecordAsync(
                new WorldEdit(WorldEntityType.Character, playerId, "player",
                    $"{player.FirstName} {player.LastName}", null, DateTime.UtcNow, act),
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
        long act,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Edits.RecordAsync(
                new WorldEdit(type, entityId, field, before, after, DateTime.UtcNow, act),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
