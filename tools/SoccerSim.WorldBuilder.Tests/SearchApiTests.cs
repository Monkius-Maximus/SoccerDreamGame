using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The global search and the register (ROADMAP.md Sprint 9), over the real batch. Shares one
/// fixture: neither reads anything it could change.
/// </summary>
public sealed class SearchApiTests : IClassFixture<WorldBuilderApp>
{
    private readonly HttpClient _client;

    public SearchApiTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> SearchAsync(string query) =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/search?q={Uri.EscapeDataString(query)}"))!;

    private static JsonNode? Group(JsonNode result, string category) =>
        result["groups"]!.AsArray()
            .FirstOrDefault(group => group!["category"]!.GetValue<string>() == category);

    private static IEnumerable<string> Labels(JsonNode result, string category) =>
        Group(result, category)?["hits"]!.AsArray().Select(hit => hit!["label"]!.GetValue<string>())
        ?? [];

    // ------------------------------------------------------------- the search

    [Fact]
    public async Task ItFindsAClubByItsName()
    {
        JsonNode result = await SearchAsync("Carioca");

        Assert.Contains("Carioca Sul", Labels(result, "Club"));

        // Four, not three: the official name is searched as well as the short one, and the fourth
        // club of Rio is "Pó de Arroz Carioca". A search that only read the short name would hide
        // exactly the club the author could not remember the short name of.
        Assert.Equal(4, Group(result, "Club")!["total"]!.GetValue<int>());
        Assert.Contains("Pó de Arroz", Labels(result, "Club"));
    }

    /// <summary>
    /// The batch is Portuguese and nobody types the accent when they are looking for something.
    /// Matching is on a normalised copy; what comes back is always the original.
    /// </summary>
    [Fact]
    public async Task AccentsAndCaseAreIgnoredOnTheWayIn_AndKeptOnTheWayOut()
    {
        JsonNode result = await SearchAsync("po de arroz");

        Assert.Equal("Pó de Arroz", Assert.Single(Labels(result, "Club")));
    }

    /// <summary>Opening a player means opening the club page they live on, so every player hit
    /// carries the club to open — and nothing else does.</summary>
    [Fact]
    public async Task OnlyAPlayerHitCarriesAClubToOpen()
    {
        JsonNode result = await SearchAsync("rio");

        Assert.All(
            Group(result, "Player")!["hits"]!.AsArray(),
            hit => Assert.NotNull(hit!["clubId"]));

        Assert.All(
            Group(result, "GeoNode")!["hits"]!.AsArray(),
            hit => Assert.Null(hit!["clubId"]));
    }

    /// <summary>The seven categories the roadmap lists. A query that hits them all proves the
    /// sweep is not quietly skipping one.</summary>
    [Theory]
    [InlineData("Carioca Sul", "Club")]
    [InlineData("Brasileiro", "Competition")]
    [InlineData("BRA", "Country")]
    [InlineData("Rio de Janeiro", "GeoNode")]
    [InlineData("CBF", "Source")]
    [InlineData("eurToBrl", "CalibrationConstant")]
    public async Task EveryCategoryIsSwept(string query, string category)
    {
        Assert.NotNull(Group(await SearchAsync(query), category));
    }

    [Fact]
    public async Task APlayerIsFoundByTheirOwnName()
    {
        // Taken from the batch itself rather than typed in, so the test does not go stale the day
        // somebody regenerates a squad.
        JsonArray squad = JsonNode.Parse(
            await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray();
        string surname = squad[0]!["lastName"]!.GetValue<string>();

        Assert.Contains(
            Labels(await SearchAsync(surname), "Player"),
            label => label.Contains(surname, StringComparison.Ordinal));
    }

    /// <summary>An exact match outranks a substring buried in a note, which is the whole reason
    /// hits carry a rank at all.</summary>
    [Fact]
    public async Task AnExactMatchComesFirst()
    {
        JsonNode result = await SearchAsync("Brasil");
        JsonArray nodes = Group(result, "GeoNode")!["hits"]!.AsArray();

        Assert.Equal("Brasil", nodes[0]!["label"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   ")]
    public async Task AQueryTooShortToMeanAnything_AnswersNothingRatherThanEverything(string query)
    {
        JsonNode result = await SearchAsync(query);

        Assert.Equal(0, result["total"]!.GetValue<int>());
        Assert.Empty(result["groups"]!.AsArray());
    }

    [Fact]
    public async Task AMissingQueryIsTheSameAsAnEmptyOne()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, JsonNode.Parse(await response.Content.ReadAsStringAsync())!["total"]!.GetValue<int>());
    }

    /// <summary>
    /// No category may bury the rest: "rio" matches 158 of the 688 players, and a screen handed
    /// all of them would be a screen with one answer on it. The group still reports its true
    /// count, so the page can say how many were not drawn.
    /// </summary>
    [Fact]
    public async Task ACrowdedCategoryIsCapped_ButStillReportsHowManyThereWere()
    {
        JsonNode players = Group(await SearchAsync("rio"), "Player")!;

        Assert.Equal(12, players["hits"]!.AsArray().Count);
        Assert.True(players["total"]!.GetValue<int>() > 12);
    }

    // ----------------------------------------------------------- the register

    /// <summary>One model (ADR-0012 §2): the pilot league is a "Divisão" — a national league with
    /// a pyramid level — with its rounds derived from its stage.</summary>
    [Fact]
    public async Task TheRegisterListsThePilotLeague_AsLevelOneOfBrazil()
    {
        JsonArray rows = JsonNode.Parse(await _client.GetStringAsync("/api/register"))!.AsArray();

        JsonNode pilot = Assert.Single(rows)!;

        Assert.Equal("Divisão", pilot["kind"]!.GetValue<string>());
        Assert.Equal(1, pilot["level"]!.GetValue<int>());
        Assert.Equal("Brasil", pilot["countryName"]!.GetValue<string>());
        Assert.Equal(20, pilot["clubs"]!.GetValue<int>());
        Assert.Equal(20, pilot["participants"]!.GetValue<int>());
        Assert.Equal(38, pilot["rounds"]!.GetValue<int>());
        Assert.Equal(380, pilot["matches"]!.GetValue<int>());
    }

    [Fact]
    public async Task ANewLevel_AppearsInTheRegister_WithItsMovesBothWays()
    {
        // Its own instance: this is the one test here that writes, and the rest share a fixture
        // that would then be describing a world they did not expect.
        using var app = new WorldBuilderApp();
        HttpClient client = app.CreateClient();

        await client.PostAsJsonAsync(
            "/api/countries/BRA/divisions",
            new { competitionId = "div_bra_2", name = "Série B", anchorGeoNodeId = "geo_bra", legs = 2, clubCount = 20, exchange = 4 });

        JsonArray rows = JsonNode.Parse(await client.GetStringAsync("/api/register"))!.AsArray();

        Assert.Equal([1, 2], rows.Select(row => row!["level"]!.GetValue<int>()));
        Assert.Equal((0, 4), (rows[0]!["up"]!.GetValue<int>(), rows[0]!["down"]!.GetValue<int>()));
        Assert.Equal((4, 0), (rows[1]!["up"]!.GetValue<int>(), rows[1]!["down"]!.GetValue<int>()));
        Assert.Equal(0, rows[1]!["participants"]!.GetValue<int>());
    }
}
