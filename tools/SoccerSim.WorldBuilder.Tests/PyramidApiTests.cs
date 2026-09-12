using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The multi-league authoring screen (ROADMAP.md Sprint 9). Its own fixture: every test here
/// builds a pyramid that did not exist, which is the whole point — the pilot batch has one country
/// and one competition, and none of this could be exercised before the screen existed.
/// </summary>
public sealed class PyramidApiTests : IAsyncLifetime
{
    private const string Brazil = "BRA";

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

    private static JsonArray Divisions(JsonArray countries, string countryId) =>
        Country(countries, countryId)["pyramid"]!["divisions"]!.AsArray();

    private async Task<JsonArray> AddDivisionAsync(string divisionId, string name, int clubCount = 20)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            new { divisionId, name, format = "LeagueDouble", clubCount });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"]!.GetValue<string>();

    // ------------------------------------------------------------- the country

    [Fact]
    public async Task ThePilotWorld_HasOneCountryAndNoPyramid()
    {
        JsonArray countries = await CountriesAsync();

        JsonNode brazil = Assert.Single(countries)!;
        Assert.Equal(Brazil, brazil["country"]!["countryId"]!.GetValue<string>());
        Assert.Equal(20, brazil["clubs"]!.GetValue<int>());
        Assert.Empty(brazil["pyramid"]!["divisions"]!.AsArray());

        // Every club of the country is listed, and none of them plays anywhere yet.
        Assert.Equal(20, brazil["roster"]!.AsArray().Count);
        Assert.All(brazil["roster"]!.AsArray(), club => Assert.Null(club!["divisionId"]));
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
    public async Task DivisionsEnterAtTheBottom_SoTheTiersCannotHaveAHole()
    {
        await AddDivisionAsync("div_bra_1", "Série A");
        JsonArray countries = await AddDivisionAsync("div_bra_2", "Série B");

        JsonArray divisions = Divisions(countries, Brazil);

        Assert.Equal([1, 2], divisions.Select(division => division!["tier"]!.GetValue<int>()));
        Assert.Equal("Série B", divisions[1]!["name"]!.GetValue<string>());

        // A new division promotes and relegates nobody: the flow is a decision, and a number
        // guessed here would balance the pyramid without anyone having made it.
        Assert.Equal(0, divisions[1]!["promotedIn"]!.GetValue<int>());
        Assert.Equal(0, divisions[1]!["relegatedOut"]!.GetValue<int>());
    }

    [Fact]
    public async Task DeletingADivisionClosesTheGapBehindIt()
    {
        await AddDivisionAsync("div_bra_1", "Série A");
        await AddDivisionAsync("div_bra_2", "Série B");
        await AddDivisionAsync("div_bra_3", "Série C");

        HttpResponseMessage response = await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonArray divisions = Divisions(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray(), Brazil);

        Assert.Equal([1, 2], divisions.Select(division => division!["tier"]!.GetValue<int>()));
        Assert.Equal(["Série A", "Série C"], divisions.Select(division => division!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task RoundsAndMatchesAreDerived_NeverTyped()
    {
        await AddDivisionAsync("div_bra_1", "Série A", clubCount: 20);

        JsonNode division = Divisions(await CountriesAsync(), Brazil)[0]!;

        // The pilot league's own numbers, and the real Brasileirão's.
        Assert.Equal(38, division["shape"]!["rounds"]!.GetValue<int>());
        Assert.Equal(380, division["shape"]!["matches"]!.GetValue<int>());
    }

    /// <summary>
    /// A field the format cannot use has no shape, and a number invented for it would be a fixture
    /// list nobody can build. This used to throw while the response was being serialized — a 500
    /// for a division the author is still in the middle of describing.
    /// </summary>
    [Fact]
    public async Task AFieldTheFormatCannotUse_HasNoShape_AndTheScreenStillLoads()
    {
        HttpResponseMessage created = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            // Groups of four do not divide eighteen.
            new { divisionId = "div_bra_1", name = "Copa", format = "GroupsKnockout", clubCount = 18 });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        JsonArray countries = await CountriesAsync();
        Assert.Null(Divisions(countries, Brazil)[0]!["shape"]);

        Assert.Contains(
            Country(countries, Brazil)["findings"]!.AsArray(),
            finding => finding!["code"]!.GetValue<string>() == "FORMAT_UNPLAYABLE");
    }

    /// <summary>Two is the floor the schema states. Refused with a reason rather than left to
    /// become a constraint violation from inside the database.</summary>
    [Fact]
    public async Task ADivisionOfFewerThanTwoClubs_IsRefused()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            new { divisionId = "div_bra_1", name = "Série A", format = "LeagueDouble", clubCount = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ao menos dois clubes", await ErrorAsync(response));
        Assert.Empty(Divisions(await CountriesAsync(), Brazil));
    }

    [Fact]
    public async Task ADivisionIsRewrittenInPlace_AndTheFlowIsCheckedAfterwards()
    {
        await AddDivisionAsync("div_bra_1", "Série A");
        await AddDivisionAsync("div_bra_2", "Série B");

        HttpResponseMessage response = await _client.PutAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_1",
            new
            {
                name = "Brasileirão Série A",
                format = "LeagueDouble",
                clubCount = 20,
                promotedIn = 4,
                relegatedOut = 4,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonArray countries = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        JsonArray divisions = Divisions(countries, Brazil);

        Assert.Equal("Brasileirão Série A", divisions[0]!["name"]!.GetValue<string>());

        // The tier is not something the rewrite can touch: it is the division's place in the
        // pyramid, and only adding and removing levels moves it.
        Assert.Equal(1, divisions[0]!["tier"]!.GetValue<int>());

        // Four up and four down is the real Brasileirão, and it balances: the four Série A
        // relegates are the four Série B receives, both ways round.
        Assert.DoesNotContain(
            Country(countries, Brazil)["findings"]!.AsArray(),
            finding => finding!["code"]!.GetValue<string>() == "PYRAMID_FLOW");
    }

    /// <summary>
    /// Half a declared flow is the failure the rule exists for: the division changes size every
    /// season, silently, and only visibly three seasons later.
    /// </summary>
    [Fact]
    public async Task ADivisionThatRelegatesWithoutPromoting_IsReportedAsUnbalanced()
    {
        await AddDivisionAsync("div_bra_1", "Série A");
        await AddDivisionAsync("div_bra_2", "Série B");

        HttpResponseMessage response = await _client.PutAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_1",
            new { name = "Série A", format = "LeagueDouble", clubCount = 20, promotedIn = 0, relegatedOut = 4 });

        JsonArray countries = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();

        Assert.Contains(
            Country(countries, Brazil)["findings"]!.AsArray(),
            finding => finding!["code"]!.GetValue<string>() == "PYRAMID_FLOW");
    }

    [Fact]
    public async Task ADivisionCannotPromoteAndRelegateMoreClubsThanItHas()
    {
        await AddDivisionAsync("div_bra_1", "Série A", clubCount: 20);

        HttpResponseMessage response = await _client.PutAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_1",
            new { name = "Série A", format = "LeagueDouble", clubCount = 20, promotedIn = 12, relegatedOut = 12 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("não cabem", await ErrorAsync(response));
    }

    // ---------------------------------------------------------- the enrolment

    [Fact]
    public async Task EnrollingAClubMovesIt_ItNeverPlaysTwoDivisions()
    {
        await AddDivisionAsync("div_bra_1", "Série A");
        await AddDivisionAsync("div_bra_2", "Série B");

        await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_1/clubs", new { clubId = "clb_bra_rio_001" });

        HttpResponseMessage moved = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_2/clubs", new { clubId = "clb_bra_rio_001" });

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        JsonArray countries = JsonNode.Parse(await moved.Content.ReadAsStringAsync())!.AsArray();
        JsonArray divisions = Divisions(countries, Brazil);

        Assert.Empty(divisions[0]!["clubIds"]!.AsArray());
        Assert.Equal("clb_bra_rio_001", divisions[1]!["clubIds"]!.AsArray().Single()!.GetValue<string>());

        // And the roster says where it plays, which is what the chips on screen are drawn from.
        JsonNode club = Country(countries, Brazil)["roster"]!.AsArray()
            .Single(entry => entry!["clubId"]!.GetValue<string>() == "clb_bra_rio_001")!;
        Assert.Equal("div_bra_2", club["divisionId"]!.GetValue<string>());

        Assert.DoesNotContain(
            Country(countries, Brazil)["findings"]!.AsArray(),
            finding => finding!["code"]!.GetValue<string>() == "CLUB_TWO_DIVISIONS");
    }

    [Fact]
    public async Task AClubCanBeWithdrawn_AndThenTheDivisionCanGo()
    {
        await AddDivisionAsync("div_bra_1", "Série A");
        await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_1/clubs", new { clubId = "clb_bra_rio_001" });

        // While a club is in it, deleting the division would drop that club out of the pyramid
        // without anyone saying where it went.
        HttpResponseMessage blocked = await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_1");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("1 clube(s) inscrito(s)", await ErrorAsync(blocked));

        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_1/clubs/clb_bra_rio_001")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_1")).StatusCode);

        Assert.Empty(Divisions(await CountriesAsync(), Brazil));
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

        HttpResponseMessage division = await _client.PostAsJsonAsync(
            "/api/countries/ARG/divisions",
            new { divisionId = "div_arg_1", name = "Primera División", format = "LeagueDouble", clubCount = 28 });
        Assert.Equal(HttpStatusCode.OK, division.StatusCode);

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/countries/ARG/divisions/div_arg_1/clubs", new { clubId = "clb_bra_rio_001" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("é de BRA, não de ARG", await ErrorAsync(response));
    }

    [Fact]
    public async Task EnrollingSomethingThatIsNotAClub_IsRefusedRatherThanStored()
    {
        await AddDivisionAsync("div_bra_1", "Série A");

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_1/clubs", new { clubId = "clb_nao_existe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Divisions(await CountriesAsync(), Brazil)[0]!["clubIds"]!.AsArray());
    }

    [Fact]
    public async Task EditingAPyramidOfACountryThatDoesNotExist_Is404()
    {
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync(
                "/api/countries/XXX/divisions",
                new { divisionId = "d", name = "n", format = "LeagueDouble", clubCount = 20 })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/countries/XXX")).StatusCode);
    }

    // --------------------------------------------------------------- undo

    /// <summary>
    /// The reason migration 0016 exists. Countries and divisions live outside the world document
    /// that the undo stack snapshots, so before that column an undo of a pyramid edit restored
    /// nothing at all while the button reported success.
    /// </summary>
    [Fact]
    public async Task EveryPyramidEdit_IsUndone()
    {
        await AddDivisionAsync("div_bra_1", "Série A");
        await AddDivisionAsync("div_bra_2", "Série B");
        await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions/div_bra_1/clubs", new { clubId = "clb_bra_rio_001" });

        Assert.Equal(
            "clb_bra_rio_001",
            Divisions(await CountriesAsync(), Brazil)[0]!["clubIds"]!.AsArray().Single()!.GetValue<string>());

        // Back past the enrolment.
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/undo", null)).StatusCode);
        Assert.Empty(Divisions(await CountriesAsync(), Brazil)[0]!["clubIds"]!.AsArray());

        // Back past the second division.
        await _client.PostAsync("/api/undo", null);
        Assert.Single(Divisions(await CountriesAsync(), Brazil));

        // Back past the first, to the pyramid-less world the batch was imported as.
        await _client.PostAsync("/api/undo", null);
        Assert.Empty(Divisions(await CountriesAsync(), Brazil));
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
        await AddDivisionAsync("div_bra_1", "Série A");
        await AddDivisionAsync("div_bra_2", "Série B");
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
                await AddDivisionAsync("div_bra_3", "Série C");
                break;

            case "rewrite-division":
                await _client.PutAsJsonAsync(
                    $"/api/countries/{Brazil}/divisions/div_bra_1",
                    new { name = "Série A", format = "LeagueSingle", clubCount = 20, promotedIn = 0, relegatedOut = 0 });
                break;

            case "delete-division":
                await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_1");
                break;

            case "enrol":
                await _client.PostAsJsonAsync(
                    $"/api/countries/{Brazil}/divisions/div_bra_1/clubs", new { clubId = "clb_bra_rio_001" });
                break;

            case "withdraw":
                await _client.DeleteAsync($"/api/countries/{Brazil}/divisions/div_bra_2/clubs/clb_bra_rio_002");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(write), write, "unknown write");
        }
    }
}
