using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The API contract: the shapes and values the club page depends on. These assert the JSON as the
/// browser receives it, because that is the actual contract — a renamed property is a broken
/// screen even when every C# type still compiles.
/// </summary>
public sealed class WorldApiTests : IClassFixture<WorldBuilderApp>
{
    private readonly HttpClient _client;

    public WorldApiTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> GetJsonAsync(string url)
    {
        HttpResponseMessage response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task World_ReportsTheLoadedBatch()
    {
        JsonNode world = await GetJsonAsync("/api/world");

        Assert.True(world["loaded"]!.GetValue<bool>());
        Assert.Equal("ClubIdentity v2", world["schemaVersion"]!.GetValue<string>());
        Assert.Equal(20, world["counts"]!["clubs"]!.GetValue<int>());
        Assert.Equal(688, world["counts"]!["characters"]!.GetValue<int>());
        Assert.Equal(18, world["counts"]!["geoNodes"]!.GetValue<int>());
        Assert.Equal(27, world["counts"]!["sources"]!.GetValue<int>());
    }

    [Fact]
    public async Task World_ExposesTheClosedEnumsInSchemaOrder()
    {
        JsonNode world = await GetJsonAsync("/api/world");
        JsonArray positions = world["enums"]!["Position"]!.AsArray();

        // The UI orders squad tables by this list, so the order is part of the contract.
        Assert.Equal(
            new[] { "GK", "CB", "FB", "DM", "CM", "AM", "WG", "ST" },
            positions.Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task World_ExposesCalibration()
    {
        JsonNode world = await GetJsonAsync("/api/world");
        JsonNode calibration = world["calibration"]!;

        Assert.Equal(1880000, calibration["constants"]!["valueBase"]!["value"]!.GetValue<double>());
        Assert.NotEmpty(calibration["constants"]!["valueBase"]!["note"]!.GetValue<string>());
    }

    [Fact]
    public async Task Clubs_ReturnsRailRowsWithServerComputedValues()
    {
        JsonArray clubs = (await GetJsonAsync("/api/clubs")).AsArray();

        Assert.Equal(20, clubs.Count);

        JsonNode carioca = clubs.Single(club => club!["clubId"]!.GetValue<string>() == "clb_bra_rio_001")!;
        Assert.Equal("CAR", carioca["displayCode"]!.GetValue<string>());
        Assert.Equal("Carioca Sul", carioca["shortName"]!.GetValue<string>());
        Assert.Equal("Rio de Janeiro", carioca["cityName"]!.GetValue<string>());
        Assert.Equal("B2", carioca["prestigeBand"]!.GetValue<string>());
        Assert.Equal("#D50A0A", carioca["primaryColor"]!.GetValue<string>());

        // Squad overall is the mean of the eleven best, computed by Core — the browser never
        // adds up a squad itself.
        Assert.Equal(81, carioca["squadOverall"]!.GetValue<int>());
        Assert.Equal("Ok", carioca["invariantLevel"]!.GetValue<string>());
    }

    [Fact]
    public async Task Clubs_FlagTheBatchesKnownInvariantViolation()
    {
        JsonArray clubs = (await GetJsonAsync("/api/clubs")).AsArray();

        // clb_bra_bel_001 has phoneticSimilarity 0.867 with NamingRule = Phonetic. It is stored,
        // served, and flagged — see docs/adr/0003.
        JsonNode remolar = clubs.Single(club => club!["clubId"]!.GetValue<string>() == "clb_bra_bel_001")!;
        Assert.Equal("Error", remolar["invariantLevel"]!.GetValue<string>());

        Assert.Single(clubs, club => club!["invariantLevel"]!.GetValue<string>() != "Ok");
    }

    [Fact]
    public async Task Club_ReturnsTheWholePageInOneResponse()
    {
        JsonNode page = await GetJsonAsync("/api/clubs/clb_bra_rio_001");
        JsonNode club = page["club"]!;

        Assert.Equal("Sociedade Carioca Sul A", club["identity"]!["officialName"]!.GetValue<string>());
        Assert.Equal("Estádio Municipal da Tijuca", club["stadium"]!["name"]!.GetValue<string>());

        // Derived values arrive computed and are not recomputed in the browser.
        Assert.Equal(104.6, club["kits"]!["deltaE"]!.GetValue<double>(), precision: 1);
        Assert.Equal(0.1439, club["kits"]!["home"]!["luminance"]!.GetValue<double>(), precision: 4);
        Assert.Equal(0.17, club["aiProfile"]!["homeAdvantageModifier"]!.GetValue<double>(), precision: 4);

        // The breadcrumb is walked server-side from the geo tree.
        Assert.Equal(
            new[] { "Mundo", "CONMEBOL", "Brasil", "Sudeste", "Rio de Janeiro" },
            page["geoPath"]!.AsArray().Select(node => node!.GetValue<string>()));

        Assert.Equal("Ok", page["invariantLevel"]!.GetValue<string>());
        Assert.Equal(6, page["findings"]!.AsArray().Count);
        Assert.Equal("Pó de Arroz", page["derbyRivalName"]!.GetValue<string>());
        Assert.Equal(12000, page["stadiumProfile"]!["min"]!.GetValue<double>());
        Assert.Equal(1.0, page["bandValueMult"]!.GetValue<double>(), precision: 4);
    }

    [Fact]
    public async Task Club_MetricsCoverTheSevenNumbersTheHeaderStripShows()
    {
        JsonNode metrics = (await GetJsonAsync("/api/clubs/clb_bra_rio_001"))["metrics"]!;

        Assert.Equal(40, metrics["playerCount"]!.GetValue<int>());
        Assert.Equal(81, metrics["overall"]!.GetValue<int>());
        Assert.True(metrics["totalMarketValueEur"]!.GetValue<long>() > 0);
        Assert.True(metrics["totalMonthlyWageBrl"]!.GetValue<long>() > 0);
        Assert.True(metrics["averageAge"]!.GetValue<double>() > 15);
        Assert.Equal(11, metrics["countByRole"]!["Titular"]!.GetValue<int>());
        Assert.Equal(4, metrics["countByPosition"]!["GK"]!.GetValue<int>());
        Assert.Equal(18, metrics["anchoredCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Squad_ReturnsThePlayersOfTheClub()
    {
        JsonArray squad = (await GetJsonAsync("/api/clubs/clb_bra_rio_001/squad")).AsArray();

        Assert.Equal(40, squad.Count);

        JsonNode keeper = squad.Single(player => player!["playerId"]!.GetValue<string>() == "plr_rio_001_0001")!;
        Assert.Equal("TOSSARO", keeper["shirtName"]!.GetValue<string>());
        Assert.Equal("GK", keeper["primaryPosition"]!.GetValue<string>());
        Assert.Equal(88, keeper["overall"]!.GetValue<int>());
        Assert.Equal(17665000, keeper["marketValueEur"]!.GetValue<int>());
        Assert.Equal(1521000, keeper["salaryMonthlyBrl"]!.GetValue<int>());
        Assert.Equal(12, keeper["attrs"]!.AsObject().Count);
        Assert.Equal(99, keeper["attrs"]!["Reflexes"]!.GetValue<int>());
    }

    [Fact]
    public async Task Enums_TravelAsNamesNotNumbers()
    {
        string json = await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad");

        // A numeric enum would still deserialize in C# and silently break every screen.
        Assert.Contains("\"primaryPosition\":\"GK\"", json);
        Assert.Contains("\"provenance\":\"Anchored\"", json);
        Assert.DoesNotContain("\"primaryPosition\":0", json);
    }

    [Fact]
    public async Task Geo_ReturnsTheTree()
    {
        JsonArray geo = (await GetJsonAsync("/api/geo")).AsArray();

        Assert.Equal(18, geo.Count);
        JsonNode root = geo.Single(node => node!["geoNodeId"]!.GetValue<string>() == "geo_world")!;
        Assert.Equal("World", root["kind"]!.GetValue<string>());

        // The root has no parent; a JSON null reads back as a null node, not a node holding null.
        Assert.Null(root["parentId"]);
    }

    [Fact]
    public async Task Competition_ReturnsItsFrozenMemberList()
    {
        JsonNode competition = await GetJsonAsync("/api/competitions/cmp_bra_tier1");

        Assert.Equal("National", competition["scope"]!.GetValue<string>());
        Assert.Equal(38, competition["rounds"]!.GetValue<int>());
        Assert.Equal(20, competition["memberClubIds"]!.AsArray().Count);
        Assert.Equal("clb_bra_rio_001", competition["memberClubIds"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("/api/clubs/clb_does_not_exist")]
    [InlineData("/api/clubs/clb_does_not_exist/squad")]
    [InlineData("/api/competitions/cmp_does_not_exist")]
    public async Task UnknownIds_Return404(string url)
    {
        HttpResponseMessage response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
