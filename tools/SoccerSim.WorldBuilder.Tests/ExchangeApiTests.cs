using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Sprint 7 over HTTP: the files come out, the diff comes back before anything is written, and
/// exporting is the one gesture that clears the pending counter.
/// </summary>
public sealed class ExchangeApiTests : IClassFixture<WorldBuilderApp>
{
    private readonly HttpClient _client;

    public ExchangeApiTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> ManifestAsync() =>
        JsonNode.Parse(await _client.GetStringAsync("/api/export"))!;

    private static object Files(params (string Tab, string Content)[] files) =>
        new { files = files.Select(file => new { tab = file.Tab, content = file.Content }).ToArray() };

    [Fact]
    public async Task TheManifest_ListsEveryTabWithItsSize()
    {
        JsonArray tabs = (await ManifestAsync())["tabs"]!.AsArray();

        Assert.Equal(12, tabs.Count);

        JsonNode clubs = tabs.First(tab => tab!["name"]!.GetValue<string>() == "Clubes")!;
        Assert.Equal("Clubes.csv", clubs["fileName"]!.GetValue<string>());
        Assert.Equal(20, clubs["rows"]!.GetValue<int>());
        Assert.Equal(33, clubs["columns"]!.GetValue<int>());
        Assert.False(clubs["authoringOnly"]!.GetValue<bool>());
        Assert.True(clubs["importable"]!.GetValue<bool>());

        // The dialog marks these because they carry real names and never go into the build.
        JsonNode audit = tabs.First(tab => tab!["name"]!.GetValue<string>() == "Audit_Jogadores")!;
        Assert.True(audit["authoringOnly"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ACsvTab_IsServedWithItsFileNameAndAByteOrderMark()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/export/csv/Kits_Estadio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Kits_Estadio.csv", response.Content.Headers.ContentDisposition!.ToString());

        string document = await response.Content.ReadAsStringAsync();

        // Without the mark Excel reads UTF-8 as the system code page and every accent breaks.
        Assert.StartsWith("﻿", document);
        Assert.Contains("clubId;displayCode;shortName", document);
    }

    [Fact]
    public async Task AnUnknownTab_Returns404_AndNamesTheOnesThatExist()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/export/csv/Escalacoes");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Kits_Estadio", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheJsonExport_IsTheWholeBase()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/export/json");
        JsonNode document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Contains("terraparalela_base_de_mundo.json", response.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal(20, document["clubs"]!.AsArray().Count);
        Assert.Equal(688, document["players"]!.AsArray().Count);
        Assert.Equal(18, document["geoNodes"]!.AsArray().Count);
        Assert.NotNull(document["calibration"]);
        Assert.NotNull(document["positionWeights"]);
    }

    [Fact]
    public async Task Exporting_ClearsThePendingCounter()
    {
        // Make something pending, then prove that exporting — and only exporting — clears it.
        await _client.PatchAsJsonAsync(
            "/api/clubs/clb_bra_rio_001",
            new
            {
                path = "audit.reviewedBy",
                value = "revisão export",
                version = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_rio_001"))!["version"]!.GetValue<long>(),
            });

        Assert.True(await _client.GetFromJsonAsync<int>("/api/pending") > 0);

        await _client.GetAsync("/api/export/csv/Clubes");

        Assert.Equal(0, await _client.GetFromJsonAsync<int>("/api/pending"));
    }

    [Fact]
    public async Task APreviewOfAnUntouchedExport_ShowsNothingAndWritesNothing()
    {
        string clubs = await Csv("Clubes");

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/import/preview", Files(("Clubes", clubs)));
        JsonNode preview = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, preview["added"]!.GetValue<int>());
        Assert.Equal(0, preview["changed"]!.GetValue<int>());
        Assert.Equal(0, preview["removed"]!.GetValue<int>());
        Assert.Empty(preview["changes"]!.AsArray());
    }

    [Fact]
    public async Task APreview_NamesTheColumnAndBothValues_WithoutWriting()
    {
        string clubs = (await Csv("Clubes")).Replace("Estádio do Umarizal", "Arena da Prévia");

        JsonNode preview = JsonNode.Parse(
            await (await _client.PostAsJsonAsync("/api/import/preview", Files(("Clubes", clubs)))).Content.ReadAsStringAsync())!;

        JsonNode change = Assert.Single(preview["changes"]!.AsArray())!;
        Assert.Equal("Changed", change["change"]!.GetValue<string>());

        JsonNode field = Assert.Single(change["fields"]!.AsArray())!;
        Assert.Equal("stadiumName", field["column"]!.GetValue<string>());
        Assert.Equal("Arena da Prévia", field["after"]!.GetValue<string>());

        // Preview writes nothing: the stored club still has its old stadium.
        JsonNode page = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_bel_001"))!;
        Assert.Equal("Estádio do Umarizal", page["club"]!["stadium"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task AMalformedFile_Returns400_WithOneProblemPerLine()
    {
        string geo = (await Csv("GeoNodes")) + "geo_broken;Planeta;geo_bra;Nome\r\n";

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/import/preview", Files(("GeoNodes", geo)));
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(body["problems"]!.AsArray());
        Assert.Contains("Planeta", body["error"]!.GetValue<string>());
    }

    /// <summary>The one destructive test: it applies a real edit and then reads it back off the
    /// club page, which is where a user would look.</summary>
    [Fact]
    public async Task ApplyingAnImport_WritesTheChangeAndCountsIt()
    {
        string clubs = (await Csv("Clubes")).Replace("Belém", "Belém do Pará");

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/import/apply", Files(("Clubes", clubs)));
        JsonNode result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result["changed"]!.GetValue<int>());
        Assert.Equal(0, result["removed"]!.GetValue<int>());

        JsonNode page = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_bel_001"))!;
        Assert.Equal("Belém do Pará", page["club"]!["geography"]!["cityName"]!.GetValue<string>());

        // An import is an edit like any other: it goes in the log and the counter notices.
        Assert.True(result["pendingEdits"]!.GetValue<int>() > 0);

        // Put it back, so the rest of the class sees the batch it expects.
        await _client.PostAsJsonAsync("/api/import/apply",
            Files(("Clubes", (await Csv("Clubes")).Replace("Belém do Pará", "Belém"))));
    }

    private async Task<string> Csv(string tab) =>
        (await _client.GetStringAsync($"/api/export/csv/{tab}")).TrimStart('﻿');
}
