using System.Text.Json;
using SoccerSim.Content.Csv;
using SoccerSim.Content.Model;
using SoccerSim.Content.Serialization;
using Xunit;

namespace SoccerSim.Content.Tests;

/// <summary>
/// The bulk-entry path. Nobody hand-types a league's worth of players into a form, so if paste
/// and CSV do not work the tool is not usable at scale — and a silently mis-parsed column is
/// worse than a rejected file.
/// </summary>
public sealed class ContentCsvTests
{
    [Fact]
    public void Export_And_Reimport_RoundTrip()
    {
        IReadOnlyList<ContentPlayer> original = TestBundles.Minimal().Players;

        string csv = ContentCsv.Write(original, ContentJson.Options);
        CsvParseResult parsed = ContentCsv.Parse(csv);

        Assert.Empty(parsed.Errors);
        Assert.Equal(original.Count, parsed.Rows.Count);

        List<ContentPlayer> reloaded = parsed.Rows
            .Select(row => JsonSerializer.Deserialize<ContentPlayer>(row.ToJsonString(), ContentJson.Options)!)
            .ToList();

        Assert.Equal(
            ContentJson.Serialize(original),
            ContentJson.Serialize(reloaded));
    }

    [Fact]
    public void Write_UsesTheRuntimeType_NotTheDeclaredOne()
    {
        // Handing the writer an IContentEntity sequence must still emit every column. Serializing
        // against the declared type would silently produce a two-column "id,key" file.
        IEnumerable<IContentEntity> entities = TestBundles.Minimal().Teams;

        string header = ContentCsv.Write(entities, ContentJson.Options).Split('\n')[0];

        Assert.Contains("name", header, StringComparison.Ordinal);
        Assert.Contains("leagueKey", header, StringComparison.Ordinal);
        Assert.Contains("eloRating", header, StringComparison.Ordinal);
    }

    [Fact]
    public void TabSeparatedPaste_IsAccepted()
    {
        // What a range copied out of Excel or Google Sheets actually looks like.
        const string pasted = "key\tname\tcode\nbrazil\tBrazil\tBRA\nspain\tSpain\tESP";

        CsvParseResult parsed = ContentCsv.Parse(pasted);

        Assert.Empty(parsed.Errors);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal("Brazil", parsed.Rows[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void NestedColumns_BuildNestedObjects()
    {
        CsvParseResult parsed = ContentCsv.Parse("key,attributes.pace,attributes.vision\nx,17,12");

        Assert.Empty(parsed.Errors);
        // Whole numbers are parsed as long; binding to an int property is the deserializer's job.
        Assert.Equal(17L, parsed.Rows[0]!["attributes"]!["pace"]!.GetValue<long>());
    }

    [Fact]
    public void PipeSeparatedCells_BecomeLists()
    {
        CsvParseResult parsed = ContentCsv.Parse("key,traitKeys\nx,hot_headed|showboat");

        var traits = parsed.Rows[0]!["traitKeys"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal(["hot_headed", "showboat"], traits);
    }

    [Fact]
    public void EmptyCell_BecomesNull_NotAnEmptyString()
    {
        // A blank optional column means "unset". Sending "" instead would fail type conversion
        // on every numeric and enum column downstream.
        CsvParseResult parsed = ContentCsv.Parse("key,nationKey\nx,");

        Assert.Null(parsed.Rows[0]!["nationKey"]);
    }

    [Fact]
    public void QuotedFieldContainingTheDelimiter_IsOneValue()
    {
        CsvParseResult parsed = ContentCsv.Parse("key,name\nx,\"Real Madrid, CF\"");

        Assert.Equal("Real Madrid, CF", parsed.Rows[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void DoubledQuotesInsideAQuotedField_Unescape()
    {
        CsvParseResult parsed = ContentCsv.Parse("key,name\nx,\"The \"\"Reds\"\"\"");

        Assert.Equal("The \"Reds\"", parsed.Rows[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void RowWithTooManyValues_IsReportedByLineNumber()
    {
        CsvParseResult parsed = ContentCsv.Parse("key,name\ngood,Fine\nbad,Too,Many,Values");

        CsvRowError error = Assert.Single(parsed.Errors);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void BlankLines_AreSkipped_NotReportedAsErrors()
    {
        CsvParseResult parsed = ContentCsv.Parse("key,name\na,Alpha\n\nb,Beta\n");

        Assert.Empty(parsed.Errors);
        Assert.Equal(2, parsed.Rows.Count);
    }

    [Fact]
    public void EmptyInput_ProducesNothing_AndNoError()
    {
        CsvParseResult parsed = ContentCsv.Parse(string.Empty);

        Assert.Empty(parsed.Rows);
        Assert.Empty(parsed.Errors);
    }
}
