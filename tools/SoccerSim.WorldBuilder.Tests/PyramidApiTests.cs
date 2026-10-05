using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The multi-league authoring screen (ROADMAP.md Sprint 9), over competitions with a level since
/// ADR-0012: the pilot league is level 1 of Brazil's pyramid, and every level below it is a
/// national league the screen adds, with the exchange between neighbours written as a pair. Its
/// own fixture per test: every test here changes the pyramid.
/// </summary>
public sealed class PyramidApiTests : IAsyncLifetime
{
    private const string Brazil = "BRA";
    private const string Pilot = "cmp_bra_tier1";

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

    private async Task<JsonArray> CountriesAsync() =>
        JsonNode.Parse(await _client.GetStringAsync("/api/countries"))!.AsArray();

    private static JsonNode Country(JsonArray countries, string countryId) =>
        countries.Single(entry => entry!["country"]!["countryId"]!.GetValue<string>() == countryId)!;

    private static JsonArray Levels(JsonArray countries, string countryId) =>
        Country(countries, countryId)["levels"]!.AsArray();

    private static IEnumerable<string> Codes(JsonArray countries, string countryId) =>
        Country(countries, countryId)["findings"]!.AsArray().Select(finding => finding!["code"]!.GetValue<string>());

    private static IReadOnlyList<string> Participants(JsonNode level) =>
        level["season"]!["participantClubIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToList();

    private async Task<JsonArray> AddDivisionAsync(string competitionId, string name, int exchange = 4, int clubCount = 20, int legs = 2)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            new { competitionId, name, anchorGeoNodeId = "geo_bra", legs, clubCount, exchange });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"]!.GetValue<string>();

    // ------------------------------------------------------------- the country

    [Fact]
    public async Task ThePilotWorld_HasOneCountry_AndThePilotLeagueIsLevelOne()
    {
        JsonArray countries = await CountriesAsync();

        JsonNode brazil = Assert.Single(countries)!;
        Assert.Equal(Brazil, brazil["country"]!["countryId"]!.GetValue<string>());
        Assert.Equal(20, brazil["clubs"]!.GetValue<int>());
        Assert.Equal(2026, brazil["year"]!.GetValue<int>());

        JsonNode top = Assert.Single(brazil["levels"]!.AsArray())!;
        Assert.Equal(1, top["level"]!.GetValue<int>());
        Assert.Equal(Pilot, top["competition"]!["competitionId"]!.GetValue<string>());
        Assert.Equal(20, Participants(top).Count);
        Assert.Equal(0.86, top["tierFloat"]!.GetValue<double>());
        Assert.Equal("Pontos corridos, turno e returno", top["stageLabel"]!.GetValue<string>());
        Assert.Empty(brazil["findings"]!.AsArray());

        // Every club of the country is listed, and every one plays in the pilot league.
        Assert.Equal(20, brazil["roster"]!.AsArray().Count);
        Assert.All(brazil["roster"]!.AsArray(), club => Assert.Equal(Pilot, club!["divisionId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ACountryIsCreatedWithItsMoneyAndNothingElse()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/countries", new
        {
            countryId = "arg",
            currency = "ars",
            eurToLocal = 1150.0,
            wageFloorMonthly = 250000,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonNode argentina = Country(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray(), "ARG");

        // Typed in lower case, stored as the code the clubs carry.
        Assert.Equal("ARS", argentina["country"]!["currency"]!.GetValue<string>());
        Assert.Equal(0, argentina["clubs"]!.GetValue<int>());

        // No mix is invented for it: the generator refuses to populate a country without one, and
        // a guessed distribution would quietly make everyone Brazilian.
        Assert.Empty(argentina["country"]!["nationalityMix"]!.AsArray());
    }

    [Theory]
    [InlineData("AR", "BRL", 5.0, 1000)]     // two letters
    [InlineData("ARG", "", 5.0, 1000)]       // no currency
    [InlineData("ARG", "ARS", 0.0, 1000)]    // no exchange rate
    [InlineData("ARG", "ARS", 5.0, -1)]      // negative floor
    public async Task AnIncompleteCountryIsRefused(string countryId, string currency, double rate, int floor)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/countries", new
        {
            countryId,
            currency,
            eurToLocal = rate,
            wageFloorMonthly = floor,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await CountriesAsync());
    }

    [Fact]
    public async Task ACountryWithClubsCannotBeDeleted()
    {
        HttpResponseMessage response = await _client.DeleteAsync($"/api/countries/{Brazil}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("20 clube(s)", await ErrorAsync(response));
        Assert.Single(await CountriesAsync());
    }

    [Fact]
    public async Task AnEmptyCountryCanBeDeleted()
    {
        await _client.PostAsJsonAsync("/api/countries", new
        {
            countryId = "URU",
            currency = "UYU",
            eurToLocal = 43.0,
            wageFloorMonthly = 30000,
        });

        Assert.Equal(2, (await CountriesAsync()).Count);
        Assert.Equal(HttpStatusCode.OK, (await _client.DeleteAsync("/api/countries/URU")).StatusCode);
        Assert.Single(await CountriesAsync());
    }

    // ----------------------------------------------------------- the pyramid

    [Fact]
    public async Task LevelsEnterAtTheBottom_WithThePairWrittenFromOneNumber()
    {
        await AddDivisionAsync("div_bra_2", "Série B", exchange: 4);
        JsonArray countries = await AddDivisionAsync("div_bra_3", "Série C", exchange: 3);

        JsonArray levels = Levels(countries, Brazil);

        Assert.Equal([1, 2, 3], levels.Select(level => level!["level"]!.GetValue<int>()));
        Assert.Equal("Série B", levels[1]!["competition"]!["name"]!.GetValue<string>());

        // The exchange with the level below, as the two counts it is made of.
        Assert.Equal((4, 4), (levels[0]!["downBelow"]!.GetValue<int>(), levels[0]!["upFromBelow"]!.GetValue<int>()));
        Assert.Equal((3, 3), (levels[1]!["downBelow"]!.GetValue<int>(), levels[1]!["upFromBelow"]!.GetValue<int>()));
        Assert.Null(levels[2]!["downBelow"]);

        // A new level starts with an empty season of the current year; it is unfilled, not broken.
        Assert.Empty(Participants(levels[1]!));
        Assert.DoesNotContain("PYRAMID_FLOW", Codes(countries, Brazil));
        Assert.Contains("SEASON_UNFILLED", Codes(countries, Brazil));
    }

    [Fact]
    public async Task DeletingALevelClosesTheGapBehindIt()
    {
        await AddDivisionAsync("div_bra_2", "Série B");
        await AddDivisionAsync("div_bra_3", "Série C");

        HttpResponseMessage response = await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonArray countries = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        JsonArray levels = Levels(countries, Brazil);

        Assert.Equal([1, 2], levels.Select(level => level!["level"]!.GetValue<int>()));
        Assert.Equal([Pilot, "div_bra_3"], levels.Select(level => level!["competition"]!["competitionId"]!.GetValue<string>()));

        // The rules that pointed at the deleted level went with it: the new neighbours exchange
        // nobody until the author says so, and the flow stays balanced.
        Assert.Equal(0, levels[0]!["downBelow"]!.GetValue<int>());
        Assert.DoesNotContain("PYRAMID_FLOW", Codes(countries, Brazil));
    }

    [Fact]
    public async Task RoundsAndMatchesAreDerived_NeverTyped()
    {
        JsonArray countries = await AddDivisionAsync("div_bra_2", "Série B", clubCount: 19, legs: 1);

        JsonArray levels = Levels(countries, Brazil);

        // The pilot league's own numbers, and the real Brasileirão's.
        Assert.Equal(38, levels[0]!["shape"]!["rounds"]!.GetValue<int>());
        Assert.Equal(380, levels[0]!["shape"]!["matches"]!.GetValue<int>());

        // An odd field in one leg: someone sits out each round.
        Assert.Equal(19, levels[1]!["shape"]!["rounds"]!.GetValue<int>());
        Assert.Equal(171, levels[1]!["shape"]!["matches"]!.GetValue<int>());
    }

    /// <summary>Two clubs and one or two legs are the floor the schema states. Refused with a
    /// reason rather than left to become a constraint violation from inside the database.</summary>
    [Theory]
    [InlineData(1, 2, "ao menos dois clubes")]
    [InlineData(20, 3, "um ou dois turnos")]
    public async Task ALevelThatCannotBePlayed_IsRefused(int clubCount, int legs, string reason)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            new { competitionId = "div_bra_2", name = "Série B", anchorGeoNodeId = "geo_bra", legs, clubCount, exchange = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await ErrorAsync(response));
        Assert.Single(Levels(await CountriesAsync(), Brazil));
    }

    /// <summary>The author states where on the map a new league is (ADR-0012 clarifications), and
    /// it has to be a country.</summary>
    [Fact]
    public async Task ALevelIsAnchoredToACountryNode()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            new { competitionId = "div_bra_2", name = "Série B", anchorGeoNodeId = "geo_city_rio", legs = 2, clubCount = 20, exchange = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("a âncora de uma divisão é um país", await ErrorAsync(response));
    }

    [Fact]
    public async Task ALevelIsRewrittenInPlace_AndItsExchangeRewritesBothRules()
    {
        await AddDivisionAsync("div_bra_2", "Série B", exchange: 4);

        HttpResponseMessage response = await _client.PutAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/{Pilot}",
            new { name = "Brasileirão Série A", legs = 2, clubCount = 20, exchange = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonArray countries = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        JsonArray levels = Levels(countries, Brazil);

        Assert.Equal("Brasileirão Série A", levels[0]!["competition"]!["name"]!.GetValue<string>());

        // The level is not something the rewrite can touch: it is the league's place in the
        // pyramid, and only adding and removing levels moves it.
        Assert.Equal(1, levels[0]!["level"]!.GetValue<int>());

        // One number, both directions: 18th–20th go down, 1st–3rd of Série B come up.
        Assert.Equal((3, 3), (levels[0]!["downBelow"]!.GetValue<int>(), levels[0]!["upFromBelow"]!.GetValue<int>()));
        Assert.Equal(18, levels[0]!["competition"]!["transitions"]![0]!["rankFrom"]!.GetValue<int>());
        Assert.DoesNotContain("PYRAMID_FLOW", Codes(countries, Brazil));
    }

    [Fact]
    public async Task AnExchangeTheFieldCannotHold_IsRefused()
    {
        await AddDivisionAsync("div_bra_2", "Série B", exchange: 4, clubCount: 6);

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            new { competitionId = "div_bra_3", name = "Série C", anchorGeoNodeId = "geo_bra", legs = 2, clubCount = 20, exchange = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("não cabem", await ErrorAsync(response));
    }

    [Fact]
    public async Task TheBottomLevel_HasNobodyBelowToExchangeWith()
    {
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/{Pilot}",
            new { name = "Série A", legs = 2, clubCount = 20, exchange = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("última divisão", await ErrorAsync(response));
    }

    // ---------------------------------------------------------- the participants

    [Fact]
    public async Task EnrollingAClubMovesIt_ItNeverPlaysTwoLevels()
    {
        await AddDivisionAsync("div_bra_2", "Série B");

        HttpResponseMessage moved = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_2/clubs", new { clubId = "clb_bra_rio_001" });

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        JsonArray countries = JsonNode.Parse(await moved.Content.ReadAsStringAsync())!.AsArray();
        JsonArray levels = Levels(countries, Brazil);

        Assert.DoesNotContain("clb_bra_rio_001", Participants(levels[0]!));
        Assert.Equal(["clb_bra_rio_001"], Participants(levels[1]!));

        // And the roster says where it plays, which is what the chips on screen are drawn from.
        JsonNode club = Country(countries, Brazil)["roster"]!.AsArray()
            .Single(entry => entry!["clubId"]!.GetValue<string>() == "clb_bra_rio_001")!;
        Assert.Equal("div_bra_2", club["divisionId"]!.GetValue<string>());

        Assert.DoesNotContain("CLUB_TWO_LEAGUES", Codes(countries, Brazil));
    }

    [Fact]
    public async Task AClubCanBeWithdrawn_AndThenTheLevelCanGo()
    {
        await AddDivisionAsync("div_bra_2", "Série B");
        await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_2/clubs", new { clubId = "clb_bra_rio_001" });

        // While a club is in it, deleting the level would drop that club out of the pyramid
        // without anyone saying where it went.
        HttpResponseMessage blocked = await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_2");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("1 clube(s) na temporada", await ErrorAsync(blocked));

        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_2/clubs/clb_bra_rio_001")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_2")).StatusCode);

        Assert.Single(Levels(await CountriesAsync(), Brazil));
    }

    [Fact]
    public async Task AClubFromAnotherCountryCannotBeEnrolled()
    {
        await _client.PostAsJsonAsync("/api/countries", new
        {
            countryId = "ARG",
            currency = "ARS",
            eurToLocal = 1150.0,
            wageFloorMonthly = 250000,
        });
        await _client.PostAsJsonAsync("/api/geo/geo_conmebol/children", new { childId = "geo_arg", displayName = "Argentina" });

        HttpResponseMessage division = await _client.PostAsJsonAsync(
            "/api/countries/ARG/divisions",
            new { competitionId = "div_arg_1", name = "Primera División", anchorGeoNodeId = "geo_arg", legs = 2, clubCount = 28, exchange = 0 });
        Assert.Equal(HttpStatusCode.OK, division.StatusCode);

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/countries/ARG/divisions/div_arg_1/clubs", new { clubId = "clb_bra_rio_001" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("é de BRA, não de ARG", await ErrorAsync(response));
    }

    [Fact]
    public async Task EnrollingSomethingThatIsNotAClub_IsRefusedRatherThanStored()
    {
        await AddDivisionAsync("div_bra_2", "Série B");

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_2/clubs", new { clubId = "clb_nao_existe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Participants(Levels(await CountriesAsync(), Brazil)[1]!));
    }

    [Fact]
    public async Task EditingAPyramidOfACountryThatDoesNotExist_Is404()
    {
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync(
                "/api/countries/XXX/divisions",
                new { competitionId = "d", name = "n", anchorGeoNodeId = "geo_bra", legs = 2, clubCount = 20, exchange = 0 })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/countries/XXX")).StatusCode);
    }

    // --------------------------------------------------------------- undo

    /// <summary>
    /// Every pyramid edit goes on the undo stack. Since ADR-0012 the levels are competitions and
    /// live in the world document itself, so undo restores them with the rest of the world.
    /// </summary>
    [Fact]
    public async Task EveryPyramidEdit_IsUndone()
    {
        await AddDivisionAsync("div_bra_2", "Série B");
        await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_2/clubs", new { clubId = "clb_bra_rio_001" });

        Assert.Equal(["clb_bra_rio_001"], Participants(Levels(await CountriesAsync(), Brazil)[1]!));

        // Back past the enrolment: the club is in the pilot again.
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/undo", null)).StatusCode);
        JsonArray levels = Levels(await CountriesAsync(), Brazil);
        Assert.Empty(Participants(levels[1]!));
        Assert.Contains("clb_bra_rio_001", Participants(levels[0]!));

        // Back past the new level, to the pilot world as imported.
        await _client.PostAsync("/api/undo", null);
        Assert.Single(Levels(await CountriesAsync(), Brazil));
    }

    [Fact]
    public async Task CreatingAndDeletingACountry_AreBothUndone()
    {
        await _client.PostAsJsonAsync("/api/countries", new
        {
            countryId = "ARG",
            currency = "ARS",
            eurToLocal = 1150.0,
            wageFloorMonthly = 250000,
        });
        Assert.Equal(2, (await CountriesAsync()).Count);

        await _client.DeleteAsync("/api/countries/ARG");
        Assert.Single(await CountriesAsync());

        // The deletion comes back...
        await _client.PostAsync("/api/undo", null);
        Assert.Equal(2, (await CountriesAsync()).Count);

        // ...and so does the creation.
        await _client.PostAsync("/api/undo", null);
        Assert.Single(await CountriesAsync());
    }

    /// <summary>
    /// The guard against forgetting, in the same shape as <see cref="UndoTests"/>: every endpoint
    /// that changes a country or a pyramid has to push a snapshot first.
    /// </summary>
    [Theory]
    [InlineData("add-country")]
    [InlineData("delete-country")]
    [InlineData("add-division")]
    [InlineData("rewrite-division")]
    [InlineData("delete-division")]
    [InlineData("enrol")]
    [InlineData("withdraw")]
    public async Task EveryScaleWrite_PushesASnapshotFirst(string write)
    {
        await AddDivisionAsync("div_bra_2", "Série B");
        await AddDivisionAsync("div_bra_3", "Série C", exchange: 2);
        await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_2/clubs", new { clubId = "clb_bra_rio_002" });

        int before = await DepthAsync();
        await PerformAsync(write);

        Assert.True(
            await DepthAsync() > before,
            $"'{write}' changed the scale data without pushing an undo snapshot.");
    }

    private async Task<int> DepthAsync() =>
        JsonNode.Parse(await _client.GetStringAsync("/api/history"))!["depth"]!.GetValue<int>();

    private async Task PerformAsync(string write)
    {
        switch (write)
        {
            case "add-country":
                await _client.PostAsJsonAsync("/api/countries", new
                {
                    countryId = "URU",
                    currency = "UYU",
                    eurToLocal = 43.0,
                    wageFloorMonthly = 30000,
                });
                break;

            case "delete-country":
                await _client.PostAsJsonAsync("/api/countries", new
                {
                    countryId = "PAR",
                    currency = "PYG",
                    eurToLocal = 7800.0,
                    wageFloorMonthly = 2500000,
                });
                await _client.DeleteAsync("/api/countries/PAR");
                break;

            case "add-division":
                await AddDivisionAsync("div_bra_4", "Série D", exchange: 2);
                break;

            case "rewrite-division":
                await _client.PutAsJsonAsync(
                    $"/api/countries/{Brazil}/divisions/{Pilot}",
                    new { name = "Série A", legs = 1, clubCount = 20, exchange = 4 });
                break;

            case "delete-division":
                await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_3");
                break;

            case "enrol":
                await _client.PostAsJsonAsync(
                    $"/api/countries/{Brazil}/divisions/div_bra_3/clubs", new { clubId = "clb_bra_rio_001" });
                break;

            case "withdraw":
                await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_2/clubs/clb_bra_rio_002");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(write), write, "unknown write");
        }
    }
}
