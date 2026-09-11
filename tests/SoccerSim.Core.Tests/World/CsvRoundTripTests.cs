using SoccerSim.Core.World;
using SoccerSim.Core.World.Serialization;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// ROADMAP.md Sprint 7's test: export, reimport, identical base — and a malformed file rejected
/// with a message per line that applies nothing.
/// </summary>
public sealed class CsvRoundTripTests
{
    /// <summary>
    /// The batch as the DATABASE holds it, not as the document spells it: the repositories
    /// recalculate every derived field on write, so a raw document is a state the tool never
    /// exports from. Comparing against one would show changes that are not there.
    /// </summary>
    private static readonly Lazy<WorldSnapshot> Source =
        new(() => WorldDerivations.Recalculate(WorldJsonReader.Read(WorldFixture.Json)));

    private static WorldSnapshot World => Source.Value;

    /// <summary>Every importable tab, exported from the world as it stands.</summary>
    private static IReadOnlyList<CsvTabFile> ExportAll(WorldSnapshot world) =>
        WorldCsv.Tabs
            .Where(tab => tab.Importable)
            .Select(tab => new CsvTabFile(tab.Name, tab.Write(world)))
            .ToList();

    [Fact]
    public void ExportingAndReimporting_ChangesNothing()
    {
        WorldImportPlan plan = WorldCsvImport.Plan(World, ExportAll(World));

        // The whole contract in one assertion: a round trip is a no-op.
        Assert.Empty(plan.Changes);
        Assert.Equal(World.Clubs.Count, plan.Result.Clubs.Count);
        Assert.Equal(World.Characters.Count, plan.Result.Characters.Count);
        Assert.Equal(World.GeoNodes, plan.Result.GeoNodes);
        Assert.Equal(World.Sources, plan.Result.Sources);
        Assert.Equal(World.Competitions.Count, plan.Result.Competitions.Count);
    }

    [Fact]
    public void ARoundTrippedWorld_ExportsToTheSameFiles()
    {
        WorldSnapshot again = WorldCsvImport.Plan(World, ExportAll(World)).Result;

        foreach (CsvTab tab in WorldCsv.Tabs)
            Assert.Equal(tab.Write(World), tab.Write(again));
    }

    // --------------------------------------------------------------------- diff

    [Fact]
    public void AChangedCell_IsReportedWithItsColumnAndBothValues()
    {
        CsvTabFile clubs = Edit("Clubes", "Estádio do Umarizal", "Arena Nova");

        WorldImportPlan plan = WorldCsvImport.Plan(World, [clubs]);

        WorldChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Changed, change.Change);
        Assert.Equal("Clubes", change.Tab);

        FieldChange field = Assert.Single(change.Fields);
        Assert.Equal("stadiumName", field.Column);
        Assert.Equal("Estádio do Umarizal", field.Before);
        Assert.Equal("Arena Nova", field.After);
    }

    [Fact]
    public void AChangeToAColour_DragsItsDerivedCellsAlong()
    {
        // The preview has to show the ΔE the change produces, not the one the file carried.
        CsvTabFile kits = Edit("Kits_Estadio", "#D50A0A", "#FFFFFF");

        WorldImportPlan plan = WorldCsvImport.Plan(World, [kits]);

        WorldChange change = plan.Changes.First(c => c.Tab == "Kits_Estadio");
        Assert.Contains(change.Fields, field => field.Column == "homeShirt");
        Assert.Contains(change.Fields, field => field.Column == "homeLuminance");
        Assert.Contains(change.Fields, field => field.Column == "deltaE_home_away");
    }

    [Fact]
    public void ARowMissingFromAPrimaryTab_IsAReMoval()
    {
        CsvTab tab = WorldCsv.Tab("GeoNodes");
        IReadOnlyList<IReadOnlyList<string>> rows = Csv.Read(tab.Write(World));

        // Drop the last node — a leaf, so nothing else points at it.
        string dropped = rows[^1][0];
        string trimmed = Csv.Write(tab.Columns, rows.Skip(1).SkipLast(1).Select(row => (IReadOnlyList<string?>)row.ToList()));

        WorldImportPlan plan = WorldCsvImport.Plan(World, [new CsvTabFile("GeoNodes", trimmed)]);

        WorldChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Removed, change.Change);
        Assert.Equal(dropped, change.Id);
        Assert.Equal(1, plan.Removed);
    }

    [Fact]
    public void ANewRowInAPrimaryTab_IsAnAddition()
    {
        CsvTab tab = WorldCsv.Tab("GeoNodes");
        string document = tab.Write(World) + "geo_new;City;geo_bra;Cidade Nova\r\n";

        WorldImportPlan plan = WorldCsvImport.Plan(World, [new CsvTabFile("GeoNodes", document)]);

        WorldChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Added, change.Change);
        Assert.Equal("geo_new", change.Id);
        Assert.Equal("Cidade Nova", change.Label);
    }

    [Fact]
    public void ANewClubNeedsItsKitsAndAuditTabsTogether()
    {
        CsvTab tab = WorldCsv.Tab("Clubes");
        IReadOnlyList<IReadOnlyList<string>> rows = Csv.Read(tab.Write(World));

        var newRow = rows[1].ToList();
        newRow[0] = "clb_bra_new_001";
        string document = Csv.Write(tab.Columns,
            rows.Skip(1).Select(row => (IReadOnlyList<string?>)row.ToList()).Append(newRow));

        var exception = Assert.Throws<WorldImportException>(
            () => WorldCsvImport.Plan(World, [new CsvTabFile("Clubes", document)]));

        Assert.Contains("clb_bra_new_001", exception.Message);
        Assert.Contains("Kits_Estadio", exception.Message);
        Assert.Contains("Audit_Clubes", exception.Message);
    }

    // ---------------------------------------------------------------- rejection

    [Fact]
    public void AMalformedCell_IsRejectedWithItsLineAndColumn()
    {
        CsvTabFile clubs = Set("Clubes", "stadiumCapacity", "quarenta mil");

        var exception = Assert.Throws<WorldImportException>(() => WorldCsvImport.Plan(World, [clubs]));

        Assert.Contains("Clubes.csv:", exception.Message);
        Assert.Contains("stadiumCapacity", exception.Message);
        Assert.Contains("quarenta mil", exception.Message);
    }

    [Fact]
    public void EveryMalformedCell_IsReportedInOnePass()
    {
        CsvTab tab = WorldCsv.Tab("Clubes");
        int capacity = tab.Columns.ToList().IndexOf("stadiumCapacity");

        // Break the same column on every row: the point is that one run names all twenty.
        var rows = Csv.Read(tab.Write(World)).Skip(1).Select(row =>
        {
            var cells = row.ToList();
            cells[capacity] = "x";
            return (IReadOnlyList<string?>)cells;
        }).ToList();

        var exception = Assert.Throws<WorldImportException>(
            () => WorldCsvImport.Plan(World, [new CsvTabFile("Clubes", Csv.Write(tab.Columns, rows))]));

        Assert.Equal(World.Clubs.Count, exception.Errors.Count);
    }

    [Fact]
    public void ADotDecimal_IsRejectedByNameRatherThanParsedAsAThousandTimesTooBig()
    {
        // 0.66 read as 66 would make a club eighty times stronger than it is, silently.
        CsvTabFile clubs = Set("Clubes", "clubStrength", "0.66");

        var exception = Assert.Throws<WorldImportException>(() => WorldCsvImport.Plan(World, [clubs]));

        Assert.Contains("decimal mark", exception.Message);
        Assert.Contains("0,66", exception.Message);
    }

    [Fact]
    public void ATruncatedLine_IsRejectedOnItsOwnLine()
    {
        string document = WorldCsv.Tab("GeoNodes").Write(World) + "geo_broken;City\r\n";

        var exception = Assert.Throws<WorldImportException>(
            () => WorldCsvImport.Plan(World, [new CsvTabFile("GeoNodes", document)]));

        Assert.Contains("has 2 cells, the header has 4", exception.Message);
    }

    [Fact]
    public void AMissingColumn_IsRejectedBeforeAnyRowIsRead()
    {
        CsvTab tab = WorldCsv.Tab("GeoNodes");
        var rows = Csv.Read(tab.Write(World)).Skip(1)
            .Select(row => (IReadOnlyList<string?>)row.Take(3).ToList())
            .ToList();

        var exception = Assert.Throws<WorldImportException>(() => WorldCsvImport.Plan(
            World,
            [new CsvTabFile("GeoNodes", Csv.Write(tab.Columns.Take(3).ToList(), rows))]));

        Assert.Contains("header is missing 1 column(s): displayName", exception.Message);
        Assert.Single(exception.Errors);
    }

    [Fact]
    public void AReadOnlyTab_IsRefusedWithTheReason()
    {
        var exception = Assert.Throws<WorldImportException>(() => WorldCsvImport.Plan(
            World,
            [new CsvTabFile("Calibracao", WorldCsv.Tab("Calibracao").Write(World))]));

        Assert.Contains("read-only", exception.Message);
    }

    [Fact]
    public void AnUnknownTab_IsRefusedAndNamesTheOnesThatExist()
    {
        var exception = Assert.Throws<WorldImportException>(
            () => WorldCsvImport.Plan(World, [new CsvTabFile("Escalacoes", "a;b\r\n")]));

        Assert.Contains("Escalacoes", exception.Message);
        Assert.Contains("Kits_Estadio", exception.Message);
    }

    [Fact]
    public void ADuplicateIdInAnAuxiliaryTab_IsRefused()
    {
        CsvTab tab = WorldCsv.Tab("Kits_Estadio");
        IReadOnlyList<IReadOnlyList<string>> rows = Csv.Read(tab.Write(World));
        string document = tab.Write(World)
            + Csv.Write(rows[1], []);

        var exception = Assert.Throws<WorldImportException>(
            () => WorldCsvImport.Plan(World, [new CsvTabFile("Kits_Estadio", document)]));

        Assert.Contains("appears more than once", exception.Message);
    }

    [Fact]
    public void AnAuxiliaryRowForAClubThatIsNotThere_IsRefused()
    {
        CsvTab tab = WorldCsv.Tab("Audit_Clubes");
        IReadOnlyList<IReadOnlyList<string>> rows = Csv.Read(tab.Write(World));

        var orphan = rows[1].ToList();
        orphan[0] = "clb_bra_ghost_001";
        string document = Csv.Write(tab.Columns,
            rows.Skip(1).Select(row => (IReadOnlyList<string?>)row.ToList()).Append(orphan));

        var exception = Assert.Throws<WorldImportException>(
            () => WorldCsvImport.Plan(World, [new CsvTabFile("Audit_Clubes", document)]));

        Assert.Contains("clb_bra_ghost_001", exception.Message);
        Assert.Contains("is not a club in this import", exception.Message);
    }

    /// <summary>Exports a tab and replaces the first occurrence of a value, so a test states the
    /// edit it is making rather than building a 33-column document by hand.</summary>
    private static CsvTabFile Edit(string tabName, string find, string replace)
    {
        string document = WorldCsv.Tab(tabName).Write(World);
        int at = document.IndexOf(find, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{find}' is not in the exported {tabName} tab.");

        return new CsvTabFile(tabName, document[..at] + replace + document[(at + find.Length)..]);
    }

    /// <summary>Exports a tab and writes one cell of the first row.</summary>
    private static CsvTabFile Set(string tabName, string column, string value)
    {
        CsvTab tab = WorldCsv.Tab(tabName);
        int index = tab.Columns.ToList().IndexOf(column);
        IReadOnlyList<IReadOnlyList<string>> rows = Csv.Read(tab.Write(World));

        var edited = rows.Skip(1).Select((row, order) =>
        {
            var cells = row.ToList();
            if (order == 0)
                cells[index] = value;
            return (IReadOnlyList<string?>)cells;
        }).ToList();

        return new CsvTabFile(tabName, Csv.Write(tab.Columns, edited));
    }
}
