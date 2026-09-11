using SoccerSim.Core.World;
using SoccerSim.Core.World.Serialization;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Sprint 7's export half: the tool can hand the world back, in both formats, without losing
/// anything it stored.
/// </summary>
public sealed class WorldExportTests
{
    private static readonly Lazy<WorldSnapshot> Source = new(() => WorldJsonReader.Read(WorldFixture.Json));

    private static WorldSnapshot World => Source.Value;

    /// <summary>
    /// The pair that matters: reading the export has to produce the world that was exported. A
    /// format that can only be read is a one-way door.
    /// </summary>
    [Fact]
    public void Json_RoundTrips()
    {
        WorldSnapshot again = WorldJsonReader.Read(WorldJsonWriter.Write(World));

        Assert.Equal(World.GeoNodes, again.GeoNodes);
        Assert.Equal(World.Sources, again.Sources);
        Assert.Equal(World.Meta, again.Meta);
        Assert.Equal(World.Clubs.Count, again.Clubs.Count);
        Assert.Equal(World.Characters.Count, again.Characters.Count);

        // Records compare collections by reference, so clubs and players are compared by the
        // same document the reader would produce from each.
        Assert.Equal(WorldJsonWriter.Write(World), WorldJsonWriter.Write(again));
    }

    [Fact]
    public void Json_RoundTripsTheCalibration()
    {
        WorldCalibration before = World.Calibration;
        WorldCalibration after = WorldJsonReader.Read(WorldJsonWriter.Write(World)).Calibration;

        Assert.Equal(before.Constants, after.Constants);
        Assert.Equal(before.AgeMult, after.AgeMult);
        Assert.Equal(before.Bands, after.Bands);
        Assert.Equal(before.HomeAdv, after.HomeAdv);
        Assert.Equal(before.StadiumProfile, after.StadiumProfile);

        foreach ((Position position, IReadOnlyDictionary<Attr, double> weights) in before.PositionWeights)
            Assert.Equal(weights, after.PositionWeights[position]);
    }

    [Fact]
    public void EveryTab_WritesAHeaderAndOneRowPerRecord()
    {
        var expected = new Dictionary<string, int>
        {
            ["Competicao"] = World.Competitions.Count,
            ["Clubes"] = World.Clubs.Count,
            ["Kits_Estadio"] = World.Clubs.Count,
            ["Jogadores"] = World.Characters.Count,
            ["Audit_Clubes"] = World.Clubs.Count,
            ["Audit_Jogadores"] = World.Characters.Count,
            ["GeoNodes"] = World.GeoNodes.Count,
            ["Fontes"] = World.Sources.Count,
            ["Pesos_Posicao"] = World.Calibration.PositionWeights.Count,
            ["Paises"] = World.Calibration.StadiumProfile.Count,
        };

        foreach (CsvTab tab in WorldCsv.Tabs)
        {
            IReadOnlyList<IReadOnlyList<string>> rows = Csv.Read(tab.Write(World));

            Assert.Equal(tab.Columns, rows[0]);
            if (expected.TryGetValue(tab.Name, out int count))
                Assert.Equal(count, rows.Count - 1);

            // Every row has exactly as many cells as the header promises — a ragged row is how a
            // column silently shifts halfway down a file.
            Assert.All(rows, row => Assert.Equal(tab.Columns.Count, row.Count));
        }
    }

    [Fact]
    public void TheTwoAuditTabs_AreMarkedAuthoringOnly()
    {
        // They carry the ReferenceAnchor — the real names — and never go into the build.
        Assert.Equal(
            ["Audit_Clubes", "Audit_Jogadores"],
            WorldCsv.Tabs.Where(tab => tab.AuthoringOnly).Select(tab => tab.Name));
    }

    [Fact]
    public void TheTabsAreTheTwelveTheRoadmapNames()
    {
        Assert.Equal(
            [
                "Leia-me", "Competicao", "Clubes", "Kits_Estadio", "Jogadores", "Audit_Clubes",
                "Audit_Jogadores", "GeoNodes", "Calibracao", "Pesos_Posicao", "Paises", "Fontes",
            ],
            WorldCsv.Tabs.Select(tab => tab.Name));
    }

    [Fact]
    public void NumbersUseTheCommaDecimalExcelPtbrExpects()
    {
        string clubs = WorldCsv.Tab("Clubes").Write(World);

        // clubStrength 0,66 — not 0.66, which Excel pt-BR would show as text.
        Assert.Contains("0,", clubs);
        Assert.DoesNotContain("0.6", clubs);
    }

    [Fact]
    public void AFieldContainingTheSeparator_IsQuoted()
    {
        // The sources carry prose with semicolons and quotes in it; this is the case that breaks
        // a naive writer.
        string document = Csv.Write(["a", "b"], [["x;y", "he said \"no\""]]);

        Assert.Contains("\"x;y\"", document);
        Assert.Contains("\"he said \"\"no\"\"\"", document);
        Assert.Equal(["x;y", "he said \"no\""], Csv.Read(document)[1]);
    }

    [Theory]
    [InlineData("a;b\r\nc;d\r\n")]
    [InlineData("a;b\nc;d\n")]
    [InlineData("a;b\nc;d")]
    [InlineData("﻿a;b\r\nc;d\r\n")]
    public void TheReader_AcceptsBothLineEndings_ATrailingRowAndAByteOrderMark(string document)
    {
        IReadOnlyList<IReadOnlyList<string>> rows = Csv.Read(document);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b"], rows[0]);
        Assert.Equal(["c", "d"], rows[1]);
    }

    [Fact]
    public void AFieldSpanningLines_SurvivesTheRoundTrip()
    {
        string document = Csv.Write(["nota"], [["linha 1\nlinha 2"]]);

        Assert.Equal(2, Csv.Read(document).Count);
        Assert.Equal("linha 1\nlinha 2", Csv.Read(document)[1][0]);
    }
}
