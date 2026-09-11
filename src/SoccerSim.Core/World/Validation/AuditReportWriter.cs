using System.Text;

namespace SoccerSim.Core.World.Validation;

/// <summary>
/// The audit sweep as a Markdown document (ROADMAP.md Sprint 8). Markdown because the report's
/// job is to leave the tool — into a pull request, a message, a note — and be readable wherever
/// it lands, without the tool being installed.
///
/// <para>It carries the sources as well as the findings. That pairing is the point: the project's
/// premise is that a number without a source does not enter, so the evidence and the exceptions
/// belong in the same document.</para>
/// </summary>
public static class AuditReportWriter
{
    public const string FileName = "auditoria_base_de_mundo.md";

    private static readonly IReadOnlyDictionary<FindingLevel, string> LevelLabel =
        new Dictionary<FindingLevel, string>
        {
            [FindingLevel.Ok] = "ok",
            [FindingLevel.Warning] = "aviso",
            [FindingLevel.Error] = "erro",
        };

    public static string Write(BatchAuditReport report, WorldSnapshot world)
    {
        var text = new StringBuilder();

        text.AppendLine("# Auditoria da base de mundo");
        text.AppendLine();
        text.AppendLine(report.Released
            ? "**Lote liberado.** Nenhum erro na varredura."
            : $"**Lote bloqueado.** {Count(report.Errors, "erro", "erros")} impedem o lote de entrar no build.");
        text.AppendLine();

        text.AppendLine("| Medida | Valor |");
        text.AppendLine("| --- | --- |");
        text.AppendLine($"| Clubes varridos | {report.ClubsScanned} |");
        text.AppendLine($"| Jogadores varridos | {report.PlayersScanned} |");
        text.AppendLine($"| Achados | {report.Findings.Count} |");
        text.AppendLine($"| Erros | {report.Errors} |");
        text.AppendLine($"| Avisos | {report.Warnings} |");
        text.AppendLine($"| Clubes afetados | {report.ClubsAffected} |");
        text.AppendLine($"| Fontes registradas | {report.SourcesRegistered} |");
        text.AppendLine();

        WriteFindings(text, report);
        WriteSources(text, world.Sources);

        return text.ToString();
    }

    private static void WriteFindings(StringBuilder text, BatchAuditReport report)
    {
        text.AppendLine("## Achados");
        text.AppendLine();

        if (report.Findings.Count == 0)
        {
            text.AppendLine("Nenhum. Todos os checks passaram em todos os registros.");
            text.AppendLine();
            return;
        }

        // Grouped by code, errors first: the grouping is what turns twenty rows into "one
        // mistake made twenty times" or "twenty different mistakes", which is a different job.
        var groups = report.Findings
            .GroupBy(finding => finding.Code)
            .OrderByDescending(group => group.Max(finding => finding.Level))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            BatchFinding first = group.First();
            text.AppendLine($"### `{group.Key}` — {first.Label} ({Count(group.Count(), "achado", "achados")})");
            text.AppendLine();

            foreach (BatchFinding finding in group.OrderBy(f => f.EntityId, StringComparer.Ordinal))
                text.AppendLine($"- **{LevelLabel[finding.Level]}** · {finding.EntityLabel} (`{finding.EntityId}`) — {finding.Detail}");

            text.AppendLine();
        }
    }

    private static void WriteSources(StringBuilder text, IReadOnlyList<WorldSource> sources)
    {
        text.AppendLine("## Fontes");
        text.AppendLine();

        var linked = sources.Where(source => source.Url is not null).ToList();
        var notes = sources.Where(source => source.Url is null).ToList();

        foreach (var group in linked.GroupBy(source => source.Tema).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"### {group.Key}");
            text.AppendLine();

            foreach (WorldSource source in group)
                text.AppendLine($"- {source.Numero ?? "—"} — [{source.Fonte ?? source.Url}]({source.Url})");

            text.AppendLine();
        }

        if (notes.Count == 0)
            return;

        // A row with no URL is a prose cross-reference, not a broken link (DATA_CONTRACT.md §5).
        // Rendering it as an empty anchor is exactly the mistake that section warns about.
        text.AppendLine("### Remissões sem URL");
        text.AppendLine();

        foreach (WorldSource source in notes)
            text.AppendLine($"- {source.Tema}");

        text.AppendLine();
    }

    private static string Count(int value, string singular, string plural) =>
        $"{value} {(value == 1 ? singular : plural)}";
}
