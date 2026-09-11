using System.Text;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Serialization;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>One row of the export dialog: what the tab is and how big it is.</summary>
public sealed record ExportTabDto(
    string Name,
    string FileName,
    int Rows,
    int Columns,
    bool AuthoringOnly,
    bool Importable);

public sealed record ExportManifestDto(IReadOnlyList<ExportTabDto> Tabs, string JsonFileName, int PendingEdits);

/// <summary>What the browser uploads: the tab name and the file's text.</summary>
public sealed record ImportRequest(IReadOnlyList<CsvTabFile> Files);

public sealed record ImportPreviewDto(
    IReadOnlyList<string> Tabs,
    int Added,
    int Changed,
    int Removed,
    IReadOnlyList<WorldChange> Changes);

public sealed record ImportResultDto(int Added, int Changed, int Removed, int PendingEdits);

/// <summary>
/// Export and import (ROADMAP.md Sprint 7): "o Excel volta a ser possível, mas como convidado,
/// não como dono". Exporting is also the one gesture that clears the pending counter — the
/// counter means "edits that have not left the tool", so only leaving the tool can clear it.
/// </summary>
internal static class ExchangeEndpoints
{
    public static void MapExchangeApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/export", GetManifestAsync);
        api.MapGet("/export/json", GetJsonAsync);
        api.MapGet("/export/csv/{tab}", GetCsvAsync);
        api.MapPost("/import/preview", PreviewAsync);
        api.MapPost("/import/apply", ApplyAsync);
    }

    private static async Task<IResult> GetManifestAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        return Results.Ok(new ExportManifestDto(
            WorldCsv.Tabs.Select(tab => new ExportTabDto(
                tab.Name,
                tab.FileName,
                tab.Rows(world).Count,
                tab.Columns.Count,
                tab.AuthoringOnly,
                tab.Importable)).ToList(),
            WorldJsonWriter.FileName,
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    private static async Task<IResult> GetJsonAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        string document = WorldJsonWriter.Write(world);

        await ClearPendingAsync(unitOfWork, cancellationToken);

        return Results.File(
            Encoding.UTF8.GetBytes(document),
            "application/json",
            WorldJsonWriter.FileName);
    }

    private static async Task<IResult> GetCsvAsync(
        string tab,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        CsvTab definition;
        try
        {
            definition = WorldCsv.Tab(tab);
        }
        catch (ArgumentException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }

        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        await ClearPendingAsync(unitOfWork, cancellationToken);

        // The byte order mark is the difference between Excel showing "São Paulo" and "SÃ£o Paulo".
        return Results.File(
            Encoding.UTF8.GetBytes(Csv.ByteOrderMark + definition.Write(world)),
            "text/csv",
            definition.FileName);
    }

    private static async Task<IResult> PreviewAsync(
        ImportRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot current = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        try
        {
            WorldImportPlan plan = WorldCsvImport.Plan(current, request.Files);

            return Results.Ok(new ImportPreviewDto(
                plan.Tabs,
                plan.Added,
                plan.Changed,
                plan.Removed,
                // The whole list, not a sample: the user is about to overwrite a base with it.
                plan.Changes));
        }
        catch (WorldImportException ex)
        {
            return Results.BadRequest(new { error = ex.Message, problems = ex.Errors });
        }
    }

    /// <summary>
    /// Writes the previewed import. The plan is recomputed from the same files rather than
    /// carried over from the preview, so what is written is what the files say — a preview held
    /// in a browser tab for an hour cannot apply a world that no longer follows from them.
    /// </summary>
    private static async Task<IResult> ApplyAsync(
        ImportRequest request,
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        WorldSnapshot current = await WorldStore.LoadAsync(unitOfWork, cancellationToken);

        WorldImportPlan plan;
        try
        {
            plan = WorldCsvImport.Plan(current, request.Files);
        }
        catch (WorldImportException ex)
        {
            return Results.BadRequest(new { error = ex.Message, problems = ex.Errors });
        }

        await WorldHistory.RecordAsync(
            unitOfWork,
            $"Importar CSV ({plan.Added} entram, {plan.Changed} mudam, {plan.Removed} saem)",
            cancellationToken);
        await WorldStore.ReplaceAsync(unitOfWork, plan.Result, cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (WorldChange change in plan.Changes)
            {
                await unitOfWork.Edits.RecordAsync(
                    new WorldEdit(
                        change.Tab is "Jogadores" or "Audit_Jogadores" ? WorldEntityType.Character : WorldEntityType.Club,
                        change.Id,
                        $"csv:{change.Tab}",
                        change.Change == ChangeKind.Added ? null : change.Label,
                        change.Change == ChangeKind.Removed ? null : change.Label,
                        DateTime.UtcNow),
                    cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Results.Ok(new ImportResultDto(
            plan.Added,
            plan.Changed,
            plan.Removed,
            await unitOfWork.Edits.CountPendingAsync(cancellationToken)));
    }

    /// <summary>Exporting is the only gesture that clears the counter (ALGORITHMS.md §8). The
    /// counter means "edits that have not left the tool"; nothing else makes them leave.</summary>
    private static async Task ClearPendingAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.Edits.MarkExportedAsync(DateTime.UtcNow, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
