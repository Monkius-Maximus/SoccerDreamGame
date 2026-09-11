using System.Text;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Competitions;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Validation;

namespace SoccerSim.WorldBuilder.Api;

/// <summary>The findings that share one code, which is how the screen groups them.</summary>
public sealed record FindingGroupDto(
    string Code,
    string Label,
    FindingLevel Level,
    int Count,
    IReadOnlyList<BatchFinding> Findings);

/// <summary>One citation, and whether it is a link or a prose cross-reference.</summary>
public sealed record SourceGroupDto(string Tema, IReadOnlyList<WorldSource> Sources);

public sealed record AuditDto(
    bool Released,
    int ClubsScanned,
    int PlayersScanned,
    int Findings,
    int Errors,
    int Warnings,
    int ClubsAffected,
    int SourcesRegistered,
    IReadOnlyList<FindingGroupDto> Groups,
    IReadOnlyList<SourceGroupDto> Sources,
    IReadOnlyList<WorldSource> Notes,
    string ReportFileName);

/// <summary>
/// The batch audit (ROADMAP.md Sprint 8). Everything here is computed, nothing stored: the sweep
/// is a question asked of the world as it stands, and caching the answer is how a screen comes to
/// show a gate that closed an hour ago.
/// </summary>
internal static class AuditEndpoints
{
    public static void MapAuditApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapGet("/audit", GetAuditAsync);
        api.MapGet("/audit/report", GetReportAsync);
    }

    private static async Task<IResult> GetAuditAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        BatchAuditReport report = BatchAudit.Run(world, await CountriesAsync(unitOfWork, cancellationToken),
            await PyramidsAsync(unitOfWork, cancellationToken));

        var groups = report.Findings
            .GroupBy(finding => finding.Code)
            // Errors first, then the codes with the most findings: the order answers "what is
            // blocking the batch" before "what else is there".
            .OrderByDescending(group => group.Max(finding => finding.Level))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new FindingGroupDto(
                group.Key,
                group.First().Label,
                group.Max(finding => finding.Level),
                group.Count(),
                group.OrderBy(finding => finding.EntityId, StringComparer.Ordinal).ToList()))
            .ToList();

        return Results.Ok(new AuditDto(
            report.Released,
            report.ClubsScanned,
            report.PlayersScanned,
            report.Findings.Count,
            report.Errors,
            report.Warnings,
            report.ClubsAffected,
            report.SourcesRegistered,
            groups,
            world.Sources
                .Where(source => source.Url is not null)
                .GroupBy(source => source.Tema)
                .Select(group => new SourceGroupDto(group.Key, group.ToList()))
                .ToList(),
            // A row with no URL is a prose cross-reference, not a broken link — the screen must
            // not render it as an empty anchor (DATA_CONTRACT.md §5).
            world.Sources.Where(source => source.Url is null).ToList(),
            AuditReportWriter.FileName));
    }

    private static Task<IReadOnlyList<CountryProfile>> CountriesAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken) =>
        unitOfWork.Countries.ListAsync(cancellationToken);

    /// <summary>One pyramid per country that has divisions. A country with none is unfinished,
    /// not broken, so there is nothing for the rules to say about it yet.</summary>
    private static async Task<IReadOnlyList<LeaguePyramid>> PyramidsAsync(
        IWorldUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var pyramids = new List<LeaguePyramid>();

        foreach (var group in (await unitOfWork.Divisions.ListAsync(cancellationToken)).Count == 0
                     ? []
                     : (await unitOfWork.Countries.ListAsync(cancellationToken)).Select(c => c.CountryId))
        {
            LeaguePyramid pyramid = await unitOfWork.Divisions.GetPyramidAsync(group, cancellationToken);
            if (pyramid.Divisions.Count > 0)
                pyramids.Add(pyramid);
        }

        return pyramids;
    }

    private static async Task<IResult> GetReportAsync(IWorldUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        WorldSnapshot world = await WorldStore.LoadAsync(unitOfWork, cancellationToken);
        BatchAuditReport report = BatchAudit.Run(world, await CountriesAsync(unitOfWork, cancellationToken),
            await PyramidsAsync(unitOfWork, cancellationToken));
        string markdown = AuditReportWriter.Write(report, world);

        return Results.File(Encoding.UTF8.GetBytes(markdown), "text/markdown", AuditReportWriter.FileName);
    }
}
