using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Sprint 10b: a club from nothing, through the API. The generator itself is covered in Core;
/// these pin what the tool adds around it — preview writes nothing, apply writes a Regen club
/// that survives every path a club travels (page, undo, JSON, CSV), and undo takes it back.
///
/// <para>Its own fixture per test, because most of them change the world on purpose.</para>
/// </summary>
public sealed class ClubGenerationApiTests : IAsyncLifetime
{
    private static readonly object Request = new { countryId = "BRA", band = "B4", clubStrength = 0.4, seed = 7 };

    private WorldBuilderApp _app = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _app = new WorldBuilderApp();
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private async Task<JsonNode> PostAsync(string path, object body)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(path, body);
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
    }

    private async Task<int> ClubCountAsync() =>
        JsonNode.Parse(await _client.GetStringAsync("/api/clubs"))!.AsArray().Count;

    private async Task<int> DepthAsync() =>
        JsonNode.Parse(await _client.GetStringAsync("/api/history"))!["depth"]!.GetValue<int>();

    private async Task<string> CsvAsync(string tab) =>
        (await _client.GetStringAsync($"/api/export/csv/{tab}")).TrimStart('﻿');

    [Fact]
    public async Task TheOptions_OfferTheCountriesWithClubProfiles()
    {
        JsonNode options = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/generate/options"))!;

        Assert.Equal(["BRA"], options["countries"]!.AsArray().Select(country => country!.GetValue<string>()));
        Assert.Contains("B4", options["bands"]!.AsArray().Select(band => band!.GetValue<string>()));
        Assert.True(options["seed"]!.GetValue<long>() > 0);
    }

    [Fact]
    public async Task APreview_IsARegenClub_AndWritesNothing()
    {
        int clubs = await ClubCountAsync();

        JsonNode first = await PostAsync("/api/clubs/generate/preview", Request);
        JsonNode second = await PostAsync("/api/clubs/generate/preview", Request);

        Assert.Equal("Regen", first["club"]!["provenance"]!.GetValue<string>());
        Assert.Null(first["club"]!["audit"]);
        Assert.Equal("B4", first["club"]!["world"]!["prestigeBand"]!.GetValue<string>());
        Assert.NotEmpty(first["geoPath"]!.AsArray());

        // Same request, same world: the same club.
        Assert.Equal(first["club"]!.ToJsonString(), second["club"]!.ToJsonString());

        Assert.Equal(clubs, await ClubCountAsync());
        Assert.Equal(0, await DepthAsync());
    }

    [Fact]
    public async Task Apply_PersistsTheClubThePreviewShowed_AsRegen()
    {
        JsonNode preview = await PostAsync("/api/clubs/generate/preview", Request);
        JsonNode result = await PostAsync("/api/clubs/generate/apply", Request);

        string clubId = result["clubId"]!.GetValue<string>();
        Assert.Equal(preview["club"]!["clubId"]!.GetValue<string>(), clubId);
        Assert.Equal(1, result["depth"]!.GetValue<int>());

        JsonNode page = JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{clubId}"))!;
        Assert.Equal("Regen", page["club"]!["provenance"]!.GetValue<string>());
        Assert.Null(page["club"]!["audit"]);
        Assert.Equal(
            preview["club"]!["identity"]!.ToJsonString(),
            page["club"]!["identity"]!.ToJsonString());

        // The club that now exists is taken: the same request draws a different club.
        JsonNode next = await PostAsync("/api/clubs/generate/preview", Request);
        Assert.NotEqual(clubId, next["club"]!["clubId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Undo_RemovesTheGeneratedClub()
    {
        int clubs = await ClubCountAsync();
        string clubId = (await PostAsync("/api/clubs/generate/apply", Request))["clubId"]!.GetValue<string>();
        Assert.Equal(clubs + 1, await ClubCountAsync());

        JsonNode undone = await PostAsync("/api/undo", new { });

        Assert.StartsWith("Gerar clube ", undone["undone"]!.GetValue<string>());
        Assert.Equal(clubs, await ClubCountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/clubs/{clubId}")).StatusCode);
    }

    [Fact]
    public async Task TheJsonExport_WritesTheRegenAuditAsNull()
    {
        string clubId = (await PostAsync("/api/clubs/generate/apply", Request))["clubId"]!.GetValue<string>();

        JsonNode document = JsonNode.Parse(await _client.GetStringAsync("/api/export/json"))!;
        JsonObject club = document["clubs"]!.AsArray().Single(entry => entry!["clubId"]!.GetValue<string>() == clubId)!.AsObject();

        Assert.True(club.ContainsKey("audit"));
        Assert.Null(club["audit"]);
    }

    /// <summary>ADR-0011 §2: Regen in the provenance column, no Audit_Clubes row — and those two
    /// tabs are enough to bring the club back after it is gone.</summary>
    [Fact]
    public async Task TheCsvExport_CarriesARegenClub_AndImportsItBack()
    {
        string clubId = (await PostAsync("/api/clubs/generate/apply", Request))["clubId"]!.GetValue<string>();

        string clubsTab = await CsvAsync("Clubes");
        string kitsTab = await CsvAsync("Kits_Estadio");

        string row = clubsTab.Split("\r\n").Single(line => line.StartsWith(clubId + ";", StringComparison.Ordinal));
        Assert.EndsWith(";Regen", row);
        Assert.DoesNotContain(clubId, await CsvAsync("Audit_Clubes"));

        await PostAsync("/api/undo", new { });
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/clubs/{clubId}")).StatusCode);

        JsonNode imported = await PostAsync("/api/import/apply", new
        {
            files = new[]
            {
                new { tab = "Clubes", content = clubsTab },
                new { tab = "Kits_Estadio", content = kitsTab },
            },
        });
        // One row added in each tab: the club and its kits.
        Assert.Equal(2, imported["added"]!.GetValue<int>());

        JsonNode page = JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{clubId}"))!;
        Assert.Equal("Regen", page["club"]!["provenance"]!.GetValue<string>());
        Assert.Null(page["club"]!["audit"]);
    }

    [Fact]
    public async Task ACountryWithoutClubProfiles_Is400_AndSaysWhy()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/clubs/generate/preview", new { countryId = "ARG", band = "B4", clubStrength = 0.4, seed = 7 });
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("import-club-profiles", body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task AStrengthOutsideTheRange_Is400_AndWritesNothing()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/clubs/generate/apply", new { countryId = "BRA", band = "B4", clubStrength = 1.5, seed = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await DepthAsync());
    }
}
