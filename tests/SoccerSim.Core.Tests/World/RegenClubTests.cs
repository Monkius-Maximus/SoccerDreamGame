using System.Text.Json;
using System.Text.Json.Nodes;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Fields;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Core.World.Validation;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// A Regen club has no anchor and therefore no deviation audit (ADR-0011 §2). These tests follow
/// that absence through everything in Core that used to assume an audit: the JSON document, the
/// CSV tabs, the field catalogue and the club checks.
/// </summary>
public sealed class RegenClubTests
{
    private const long MasterSeed = 20260814;

    private static readonly Lazy<WorldSnapshot> Pilot =
        new(() => WorldDerivations.Recalculate(WorldJsonReader.Read(WorldFixture.Json)));

    private static readonly Lazy<ClubIdentity> Generated = new(() => ClubGenerator.Generate(
        new ClubGenerationRequest("BRA", PrestigeBand.B4, 0.72, Seed: 11),
        ClubProfilesReader.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "club_profiles.json"))),
        Pilot.Value.GeoNodes,
        Pilot.Value.Calibration,
        ClubGenerationContext.From(Pilot.Value.Clubs),
        MasterSeed));

    private static ClubIdentity Regen => Generated.Value;

    /// <summary>The pilot plus one generated club, as the database would hold it.</summary>
    private static WorldSnapshot World => Pilot.Value with { Clubs = [.. Pilot.Value.Clubs, Regen] };

    private static IReadOnlyList<CsvTabFile> ExportAll(WorldSnapshot world) =>
        WorldCsv.Tabs
            .Where(tab => tab.Importable)
            .Select(tab => new CsvTabFile(tab.Name, tab.Write(world)))
            .ToList();

    // ------------------------------------------------------------------ provenance

    [Fact]
    public void Provenance_IsReadFromTheAudit()
    {
        ClubIdentity anchored = Pilot.Value.Clubs[0];

        Assert.Equal(Provenance.Anchored, anchored.Provenance);
        Assert.Equal(Provenance.Regen, (anchored with { Audit = null }).Provenance);
    }

    // ------------------------------------------------------------------ JSON

    [Fact]
    public void TheJsonDocument_WritesAnExplicitNullAudit_AndReadsItBack()
    {
        string json = WorldJsonWriter.Write(World);

        JsonObject written = JsonNode.Parse(json)!["clubs"]!.AsArray()
            .Single(club => club!["clubId"]!.GetValue<string>() == Regen.ClubId)!.AsObject();
        Assert.True(written.ContainsKey("audit"));
        Assert.Null(written["audit"]);

        ClubIdentity read = WorldDerivations.Recalculate(WorldJsonReader.Read(json))
            .Clubs.Single(club => club.ClubId == Regen.ClubId);
        Assert.Equal(JsonSerializer.Serialize(Regen), JsonSerializer.Serialize(read));
    }

    [Fact]
    public void AClubWithNoAuditKeyAtAll_IsRejected_NotReadAsRegen()
    {
        JsonObject root = JsonNode.Parse(WorldJsonWriter.Write(World))!.AsObject();
        root["clubs"]!.AsArray()[0]!.AsObject().Remove("audit");

        var ex = Assert.Throws<WorldImportException>(() => WorldJsonReader.Read(root.ToJsonString()));

        Assert.Contains(ex.Errors, error => error.Contains("audit") && error.Contains("missing required field"));
    }

    // ------------------------------------------------------------------ CSV

    [Fact]
    public void TheCsvExport_MarksProvenance_AndGivesTheRegenClubNoAuditRow()
    {
        string clubs = WorldCsv.Tab("Clubes").Write(World);
        string audits = WorldCsv.Tab("Audit_Clubes").Write(World);

        Assert.Contains("provenance", clubs.Split('\n')[0]);
        Assert.Contains(clubs.Split('\n'), line => line.StartsWith(Regen.ClubId) && line.TrimEnd('\r').EndsWith(";Regen"));
        Assert.DoesNotContain(Regen.ClubId, audits);
        Assert.Equal(Pilot.Value.Clubs.Count, audits.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
    }

    [Fact]
    public void ACsvRoundTrip_WithARegenClub_ChangesNothing()
    {
        WorldImportPlan plan = WorldCsvImport.Plan(World, ExportAll(World));

        Assert.Empty(plan.Changes);
        Assert.Null(plan.Result.Clubs.Single(club => club.ClubId == Regen.ClubId).Audit);
    }

    [Fact]
    public void ANewRegenClub_ImportsWithClubesAndKitsAlone()
    {
        WorldImportPlan plan = WorldCsvImport.Plan(Pilot.Value,
        [
            new CsvTabFile("Clubes", WorldCsv.Tab("Clubes").Write(World)),
            new CsvTabFile("Kits_Estadio", WorldCsv.Tab("Kits_Estadio").Write(World)),
        ]);

        // One addition per tab the club arrives in, and nothing else moves.
        Assert.Equal(new[] { "Clubes", "Kits_Estadio" }, plan.Changes.Select(change => change.Tab).Order());
        Assert.All(plan.Changes, change =>
        {
            Assert.Equal(ChangeKind.Added, change.Change);
            Assert.Equal(Regen.ClubId, change.Id);
        });
        Assert.Null(plan.Result.Clubs.Single(club => club.ClubId == Regen.ClubId).Audit);
    }

    [Fact]
    public void ARegenClubWithAnAuditRow_IsRefused()
    {
        // An audit row carrying the Regen club's id, borrowed from a pilot club.
        ClubIdentity donor = Pilot.Value.Clubs[0];
        ClubIdentity impostor = donor with { ClubId = Regen.ClubId, Audit = donor.Audit! with { ClubId = Regen.ClubId } };
        string audits = WorldCsv.Tab("Audit_Clubes").Write(Pilot.Value with { Clubs = [.. Pilot.Value.Clubs, impostor] });

        var ex = Assert.Throws<WorldImportException>(() => WorldCsvImport.Plan(World,
        [
            new CsvTabFile("Clubes", WorldCsv.Tab("Clubes").Write(World)),
            new CsvTabFile("Audit_Clubes", audits),
        ]));

        Assert.Contains(ex.Errors, error => error.Contains($"{Regen.ClubId} is a Regen club"));
    }

    [Fact]
    public void ChangingProvenanceOnImport_IsRefused()
    {
        ClubIdentity anchored = Pilot.Value.Clubs[0];
        string[] lines = WorldCsv.Tab("Clubes").Write(Pilot.Value).Split('\n');
        int row = Array.FindIndex(lines, line => line.StartsWith(anchored.ClubId + ";", StringComparison.Ordinal));
        lines[row] = lines[row].Replace(";Anchored", ";Regen", StringComparison.Ordinal);

        var ex = Assert.Throws<WorldImportException>(() =>
            WorldCsvImport.Plan(Pilot.Value, [new CsvTabFile("Clubes", string.Join('\n', lines))]));

        Assert.Contains(ex.Errors, error => error.Contains("Provenance does not change on import"));
    }

    // ------------------------------------------------------------------ field catalogue

    [Fact]
    public void AuditFields_ReadAsEmpty_OnARegenClub()
    {
        Assert.Null(ClubFields.Read(Regen, "audit.anchorClubName"));
        Assert.Null(ClubFields.Read(Regen, "audit.anchorFoundingYear"));
        Assert.Null(ClubFields.Read(Regen, "audit.phoneticSimilarity"));
    }

    [Fact]
    public void WritingAnAuditField_OnARegenClub_IsRefusedByPath()
    {
        var ex = Assert.Throws<FieldPatchException>(() => ClubFields.Apply(Regen, "audit.note", "qualquer coisa"));

        Assert.Equal("audit.note", ex.Path);
        Assert.Contains("Regen club has no anchor", ex.Message);
    }

    // ------------------------------------------------------------------ checks

    [Fact]
    public void TheAnchorCheck_DoesNotApply_ToARegenClub()
    {
        Finding anchor = Assert.Single(ClubInvariants.Check(Regen, Pilot.Value.Calibration), finding => finding.Code == "ANCHOR_VERIFIED");

        Assert.Equal(FindingLevel.Ok, anchor.Level);
        Assert.Contains("Regen", anchor.Detail);
    }

    [Fact]
    public void TheBatchAudit_SkipsAnchorChecks_ForARegenClub()
    {
        BatchAuditReport report = BatchAudit.Run(World);

        Assert.DoesNotContain(report.Findings, finding =>
            finding.EntityId == Regen.ClubId
            && finding.Code is "FOUNDING_EQ" or "FOUNDING_DECADE" or "ANCHOR_UNVERIFIED" or "CITATION_MISSING");
    }
}
