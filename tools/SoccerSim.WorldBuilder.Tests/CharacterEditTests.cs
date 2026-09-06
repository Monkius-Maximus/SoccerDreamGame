using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The player modal's contract: the twelve attributes are editable, the economy follows from them,
/// and the position weights that explain which attributes matter come from the calibration.
/// </summary>
public sealed class CharacterEditTests : IClassFixture<WorldBuilderApp>
{
    private const string PlayerId = "plr_rio_001_0001";   // a goalkeeper, OVR 88

    private readonly HttpClient _client;

    public CharacterEditTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> GetAsync() =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/characters/{PlayerId}"))!;

    private async Task<HttpResponseMessage> PatchAsync(string path, string? value)
    {
        long version = (await GetAsync())["version"]!.GetValue<long>();
        return await _client.PatchAsJsonAsync($"/api/characters/{PlayerId}", new { path, value, version });
    }

    [Fact]
    public async Task ThePageCarriesThePositionWeights()
    {
        JsonNode page = await GetAsync();
        JsonNode weights = page["positionWeights"]!;

        // A keeper's overall is mostly reflexes and handling; finishing is worth nothing. This is
        // what the modal's per-attribute weight column shows.
        Assert.Equal(0.26, weights["Reflexes"]!.GetValue<double>(), precision: 4);
        Assert.Equal(0.20, weights["Handling"]!.GetValue<double>(), precision: 4);
        Assert.Equal(0.00, weights["Finishing"]!.GetValue<double>(), precision: 4);
    }

    [Fact]
    public async Task StoredAndRecalculatedAgree_OnAFreshlyImportedBatch()
    {
        // Every write recalculates, so a divergence can only come from a calibration re-fit. On an
        // untouched import there is none — which is also the Sprint 1 gate holding, seen from here.
        JsonNode page = await GetAsync();

        Assert.False(page["economyDiverges"]!.GetValue<bool>());
        Assert.Equal(
            page["stored"]!["marketValueEur"]!.GetValue<int>(),
            page["recalculated"]!["marketValueEur"]!.GetValue<int>());
    }

    [Fact]
    public async Task PatchingAnAttribute_MovesTheEconomy()
    {
        JsonNode before = await GetAsync();
        int overallBefore = before["character"]!["overall"]!.GetValue<int>();

        // Reflexes is the keeper's heaviest attribute, so dropping it must drop the overall.
        HttpResponseMessage response = await PatchAsync("attrs.Reflexes", "40");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonNode after = await GetAsync();
        Assert.True(after["character"]!["overall"]!.GetValue<int>() < overallBefore);
        Assert.True(
            after["character"]!["marketValueEur"]!.GetValue<int>() < before["character"]!["marketValueEur"]!.GetValue<int>(),
            "a weaker player must be worth less");

        await PatchAsync("attrs.Reflexes", "99");
        Assert.Equal(overallBefore, (await GetAsync())["character"]!["overall"]!.GetValue<int>());
    }

    [Fact]
    public async Task PatchingTheSurname_RewritesTheShirtName()
    {
        await PatchAsync("lastName", "Guimarães");

        Assert.Equal("GUIMARÃES", (await GetAsync())["character"]!["shirtName"]!.GetValue<string>());

        await PatchAsync("lastName", "Tossaro");
    }

    [Theory]
    [InlineData("attrs.Reflexes", "150")]      // outside 1–99
    [InlineData("attrs.Reflexes", "0")]
    [InlineData("attrs.Telepathy", "50")]      // not an attribute
    [InlineData("shirtNumber", "0")]           // outside 1–99
    [InlineData("primaryPosition", "Sweeper")] // not in the closed set
    [InlineData("preferredFoot", "Either")]
    public async Task InvalidValues_Return400(string path, string value)
    {
        HttpResponseMessage response = await PatchAsync(path, value);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CalculatedEconomyFields_CannotBePatched()
    {
        HttpResponseMessage response = await PatchAsync("marketValueEur", "99000000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("recalculated on write", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SecondaryPositions_RoundTripThroughThePipeFormat()
    {
        await PatchAsync("secondaryPositions", "CB|DM");

        JsonNode character = (await GetAsync())["character"]!;
        Assert.Equal(new[] { "CB", "DM" }, character["secondaryPositions"]!.AsArray().Select(node => node!.GetValue<string>()));

        await PatchAsync("secondaryPositions", "");
        Assert.Empty((await GetAsync())["character"]!["secondaryPositions"]!.AsArray());
    }

    [Fact]
    public async Task Recalculate_RewritesTheEconomyAndCountsAsAnEdit()
    {
        long version = (await GetAsync())["version"]!.GetValue<long>();
        int pending = await _client.GetFromJsonAsync<int>("/api/pending");

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/characters/{PlayerId}/recalculate", new { path = "economy", value = (string?)null, version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(pending + 1, await _client.GetFromJsonAsync<int>("/api/pending"));
        Assert.False((await GetAsync())["economyDiverges"]!.GetValue<bool>());
    }

    [Fact]
    public async Task UnknownPlayer_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/characters/plr_nope")).StatusCode);

        HttpResponseMessage patch = await _client.PatchAsJsonAsync(
            "/api/characters/plr_nope", new { path = "firstName", value = "x", version = 1L });
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }
}
