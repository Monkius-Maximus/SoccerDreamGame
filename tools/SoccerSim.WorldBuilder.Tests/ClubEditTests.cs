using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Sprint 4's contract: a valid patch persists and drags its derived fields with it; an invalid
/// one returns 400 and writes nothing.
/// </summary>
public sealed class ClubEditTests : IClassFixture<WorldBuilderApp>
{
    private const string ClubId = "clb_bra_rio_001";

    private readonly HttpClient _client;

    public ClubEditTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> GetClubAsync() =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{ClubId}"))!;

    /// <summary>The version travels with the page, so a form patches the version it is showing.</summary>
    private async Task<long> CurrentVersionAsync() => (await GetClubAsync())["version"]!.GetValue<long>();

    private async Task<HttpResponseMessage> PatchAsync(string path, string? value, long? version = null) =>
        await _client.PatchAsJsonAsync($"/api/clubs/{ClubId}", new
        {
            path,
            value,
            version = version ?? await CurrentVersionAsync(),
        });

    [Fact]
    public async Task ValidPatch_Persists()
    {
        HttpResponseMessage response = await PatchAsync("identity.nickname", "Os Rubro-Negros");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonNode club = (await GetClubAsync())["club"]!;
        Assert.Equal("Os Rubro-Negros", club["identity"]!["nickname"]!.GetValue<string>());
    }

    [Fact]
    public async Task PatchingAKitColour_RecalculatesDeltaEAndLuminance()
    {
        // The point of recalculating on write: the user changes a colour, and every number that
        // followed from it moves in the same request.
        await PatchAsync("kits.home.shirt", "#FFFFFF");

        JsonNode club = (await GetClubAsync())["club"]!;
        Assert.Equal("#FFFFFF", club["kits"]!["home"]!["shirt"]!.GetValue<string>());
        Assert.Equal(1.0, club["kits"]!["home"]!["luminance"]!.GetValue<double>(), precision: 4);

        // White home against a white away kit: ΔE collapses and the invariant turns red.
        Assert.Equal(0.0, club["kits"]!["deltaE"]!.GetValue<double>(), precision: 1);
        Assert.Equal("Error", (await GetClubAsync())["invariantLevel"]!.GetValue<string>());

        await PatchAsync("kits.home.shirt", "#D50A0A");   // put it back for the other tests
    }

    [Fact]
    public async Task PatchingTheAtmosphere_RewritesHomeAdvantageFromCalibration()
    {
        await PatchAsync("stadium.atmosphereArchetype", "Apathetic");

        JsonNode club = (await GetClubAsync())["club"]!;
        Assert.Equal("Apathetic", club["stadium"]!["atmosphereArchetype"]!.GetValue<string>());
        Assert.Equal(0.0, club["aiProfile"]!["homeAdvantageModifier"]!.GetValue<double>(), precision: 4);

        await PatchAsync("stadium.atmosphereArchetype", "Cauldron");
        Assert.Equal(0.17, (await GetClubAsync())["club"]!["aiProfile"]!["homeAdvantageModifier"]!.GetValue<double>(), precision: 4);
    }

    [Fact]
    public async Task PatchingThePalette_RewritesTheCrestColours()
    {
        await PatchAsync("palette.tertiary", "#00FF00");

        JsonNode club = (await GetClubAsync())["club"]!;
        Assert.Equal("#00FF00", club["crest"]!["colors"]!.AsArray()[2]!.GetValue<string>());

        await PatchAsync("palette.tertiary", "#FFFFFF");
    }

    [Fact]
    public async Task InvalidEnum_Returns400_AndWritesNothing()
    {
        string before = (await GetClubAsync())["club"]!["stadium"]!["pitchSurface"]!.GetValue<string>();

        HttpResponseMessage response = await PatchAsync("stadium.pitchSurface", "Astroturf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string error = await response.Content.ReadAsStringAsync();
        Assert.Contains("Astroturf", error);
        Assert.Contains("Pristine", error);   // the allowed values are named

        Assert.Equal(before, (await GetClubAsync())["club"]!["stadium"]!["pitchSurface"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("stadium.capacity", "not-a-number")]
    [InlineData("palette.primary", "red")]
    [InlineData("world.squadSize", "12")]           // outside 28–40
    [InlineData("identity.officialName", "")]       // required
    [InlineData("audit.nicknameCommercialLevel", "7")]
    public async Task InvalidValues_Return400(string path, string value)
    {
        HttpResponseMessage response = await PatchAsync(path, value);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DerivedFields_CannotBePatched_AndTheErrorSaysWhy()
    {
        HttpResponseMessage response = await PatchAsync("aiProfile.homeAdvantageModifier", "0.99");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("recalculated on write", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownField_Returns400()
    {
        HttpResponseMessage response = await PatchAsync("stadium.roofColour", "#000000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unknown field", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownClub_Returns404()
    {
        HttpResponseMessage response = await _client.PatchAsJsonAsync(
            "/api/clubs/clb_does_not_exist",
            new { path = "identity.nickname", value = "x", version = 1L });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditingBumpsTheVersion_AndThePendingCounter()
    {
        int pendingBefore = await _client.GetFromJsonAsync<int>("/api/pending");
        long versionBefore = await CurrentVersionAsync();

        HttpResponseMessage response = await PatchAsync("audit.reviewedBy", "revisão 2026-09");
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(versionBefore + 1, body["version"]!.GetValue<long>());
        Assert.Equal(pendingBefore + 1, body["pendingEdits"]!.GetValue<int>());
        Assert.Equal(pendingBefore + 1, await _client.GetFromJsonAsync<int>("/api/pending"));
    }

    [Fact]
    public async Task AnInvalidPatch_DoesNotTouchThePendingCounter()
    {
        int before = await _client.GetFromJsonAsync<int>("/api/pending");

        await PatchAsync("stadium.pitchSurface", "Astroturf");

        Assert.Equal(before, await _client.GetFromJsonAsync<int>("/api/pending"));
    }

    [Fact]
    public async Task TheInvariantSweepFollowsTheEdit()
    {
        // Errors are shown, not blocked: the club saves in a state its own checks reject, which
        // is exactly what lets a user fix data in the order they choose.
        await PatchAsync("audit.anchorFactsVerified", "0");

        JsonNode page = await GetClubAsync();
        Assert.Equal("Error", page["invariantLevel"]!.GetValue<string>());

        await PatchAsync("audit.anchorFactsVerified", "1");
        Assert.Equal("Ok", (await GetClubAsync())["invariantLevel"]!.GetValue<string>());
    }
}
