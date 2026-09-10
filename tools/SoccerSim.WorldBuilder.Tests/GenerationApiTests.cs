using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Sprint 5's contract over HTTP: the panel can ask for defaults, look at a squad without
/// writing one, and then write exactly the squad it looked at.
///
/// <para>
/// Generation is destructive, so these tests run against their own club in their own class
/// fixture — xUnit gives one <see cref="WorldBuilderApp"/> per test class, and no other class
/// reads this club's squad.
/// </para>
/// </summary>
public sealed class GenerationApiTests : IClassFixture<WorldBuilderApp>
{
    private const string ClubId = "clb_bra_rio_001";

    private readonly HttpClient _client;

    public GenerationApiTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> OptionsAsync() =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{ClubId}/squad/options"))!;

    private static JsonObject Request(
        int squadSize = 30,
        string formation = "4-3-3",
        int targetOverall = 70,
        string ageProfile = "Balanced",
        long seed = 4242) => new()
    {
        ["squadSize"] = squadSize,
        ["formation"] = formation,
        ["targetOverall"] = targetOverall,
        ["ageProfile"] = ageProfile,
        ["seed"] = seed,
    };

    private async Task<HttpResponseMessage> PostAsync(string verb, JsonObject request, string clubId = ClubId) =>
        await _client.PostAsJsonAsync($"/api/clubs/{clubId}/squad/{verb}", request);

    private async Task<JsonNode> PreviewAsync(JsonObject request)
    {
        HttpResponseMessage response = await PostAsync("preview", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private async Task<int> SquadSizeAsync(string clubId = ClubId) =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{clubId}/squad"))!.AsArray().Count;

    [Fact]
    public async Task Options_PrefillFromTheClub()
    {
        JsonNode options = await OptionsAsync();

        // The club's own squad size and the formation its tactical style implies — the panel
        // opens on what the club already says about itself.
        Assert.InRange(options["squadSize"]!.GetValue<int>(), 28, 40);
        Assert.Contains(options["formation"]!.GetValue<string>(), new[] { "4-3-3", "4-2-3-1", "4-4-2", "3-5-2" });
        Assert.InRange(options["targetOverall"]!.GetValue<int>(), 52, 88);
        Assert.Equal("Balanced", options["ageProfile"]!.GetValue<string>());
    }

    [Fact]
    public async Task Options_HandOutAFreshSeedEachTime()
    {
        // "Generate again" has to mean something without the user inventing a number.
        long first = (await OptionsAsync())["seed"]!.GetValue<long>();
        long second = (await OptionsAsync())["seed"]!.GetValue<long>();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Preview_WritesNothing()
    {
        int before = await SquadSizeAsync();

        await PreviewAsync(Request());

        Assert.Equal(before, await SquadSizeAsync());
    }

    [Fact]
    public async Task Preview_IsRepeatableForTheSameSeed()
    {
        // The promise the panel makes: what you looked at is what gets written.
        string first = (await PreviewAsync(Request(seed: 99))) ["squad"]!.ToJsonString();
        string second = (await PreviewAsync(Request(seed: 99)))["squad"]!.ToJsonString();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Preview_ReturnsTheRequestedSizeAndComposition()
    {
        JsonNode preview = await PreviewAsync(Request(squadSize: 34));

        Assert.Equal(34, preview["squad"]!.AsArray().Count);
        Assert.Equal(34, preview["composition"]!.AsObject().Sum(entry => entry.Value!.GetValue<int>()));
    }

    [Fact]
    public async Task Preview_DrawsElevenAcrossFourLines()
    {
        JsonArray xi = (await PreviewAsync(Request(formation: "4-2-3-1")))["startingXi"]!.AsArray();

        Assert.Equal(11, xi.Count);

        // Keeper first, attack last: the panel draws them in the order it receives them.
        int[] lines = xi.Select(slot => slot!["line"]!.GetValue<int>()).ToArray();
        Assert.Equal(lines.OrderBy(line => line), lines);
        Assert.Equal(0, lines[0]);
        Assert.Equal(3, lines[^1]);
    }

    [Fact]
    public async Task Preview_NamesWhatWouldBeReplaced()
    {
        JsonNode preview = await PreviewAsync(Request());

        // The tone of the footer note depends on these two numbers, so they are part of the
        // contract, not decoration.
        Assert.Equal(await SquadSizeAsync(), preview["existingPlayers"]!.GetValue<int>());
        Assert.True(preview["existingAnchored"]!.GetValue<int>() >= 0);
    }

    [Theory]
    [InlineData("4231", "not a formation")]
    [InlineData("4-3-3", "ok")]
    public async Task Preview_RejectsAFormationItDoesNotKnow(string formation, string expectation)
    {
        HttpResponseMessage response = await PostAsync("preview", Request(formation: formation));

        if (expectation == "ok")
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return;
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The allowed values are named, so the error is actionable from the response alone.
        string error = await response.Content.ReadAsStringAsync();
        Assert.Contains("4-2-3-1", error);
    }

    [Theory]
    [InlineData(27)]
    [InlineData(41)]
    public async Task Preview_RejectsASquadSizeOutsideTheRange(int squadSize)
    {
        HttpResponseMessage response = await PostAsync("preview", Request(squadSize: squadSize));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("28", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Preview_RejectsAnAgeProfileItDoesNotKnow()
    {
        HttpResponseMessage response = await PostAsync("preview", Request(ageProfile: "Ancient"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Experienced", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownClub_Returns404()
    {
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/clubs/clb_nope/squad/options")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await PostAsync("preview", Request(), clubId: "clb_nope")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await PostAsync("generate", Request(), clubId: "clb_nope")).StatusCode);
    }

    /// <summary>
    /// The one destructive test, deliberately last in intent: it replaces the club's squad and
    /// then proves the written players are the previewed ones.
    /// </summary>
    [Fact]
    public async Task Generate_WritesThePreviewedSquad_AndReplacesTheOldOne()
    {
        JsonObject request = Request(squadSize: 29, seed: 777);
        JsonNode preview = await PreviewAsync(request);
        int existing = preview["existingPlayers"]!.GetValue<int>();

        int pendingBefore = await _client.GetFromJsonAsync<int>("/api/pending");

        HttpResponseMessage response = await PostAsync("generate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonNode result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(29, result["written"]!.GetValue<int>());
        Assert.Equal(existing, result["replaced"]!.GetValue<int>());

        // Replacing a squad is one authoring act, so it costs one line in the edit log.
        Assert.Equal(pendingBefore + 1, result["pendingEdits"]!.GetValue<int>());

        JsonArray written = JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{ClubId}/squad"))!.AsArray();
        Assert.Equal(29, written.Count);

        string[] previewed = preview["squad"]!.AsArray()
            .Select(player => player!["playerId"]!.GetValue<string>())
            .Order()
            .ToArray();
        Assert.Equal(previewed, written.Select(player => player!["playerId"]!.GetValue<string>()).Order());

        // And the club page follows: its metrics are computed from whoever is in the squad now.
        JsonNode page = JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{ClubId}"))!;
        Assert.Equal(29, page["metrics"]!["playerCount"]!.GetValue<int>());

        // Every generated player is Regen, so the club's anchored count falls to zero — the
        // same fact the panel's replace warning is about.
        Assert.Equal(0, page["metrics"]!["anchoredCount"]!.GetValue<int>());
    }
}
