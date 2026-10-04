using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Sprint 11b: a whole division through the API. The generator and the write are covered in Core;
/// these pin what the tool adds around them — the preview table writes nothing, apply enrols the
/// batch where the Ligas tab shows it, a request the pyramid cannot hold is a 400 that writes
/// nothing, and undo takes the batch back.
///
/// <para>Its own fixture per test, because most of them change the world on purpose.</para>
/// </summary>
public sealed class DivisionGenerationApiTests : IAsyncLifetime
{
    private const string Brazil = "BRA";
    private const string Division = "div_bra_2";
    private const string Generate = $"/api/countries/{Brazil}/divisions/{Division}/generate";

    private static readonly object Request = new { clubCount = 4, band = "B4", strengthMin = 0.6, strengthMax = 0.75, seed = 2026 };

    private WorldBuilderApp _app = null!;
    private HttpClient _client = null!;

    /// <summary>The undo stack's depth once the division exists: creating it is itself a step.</summary>
    private int _baseDepth;

    /// <summary>The pilot world with an empty twenty-seat Série B below the pilot league.</summary>
    public async Task InitializeAsync()
    {
        _app = new WorldBuilderApp();
        _client = _app.CreateClient();

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            $"/api/countries/{Brazil}/divisions",
            new { competitionId = Division, name = "Série B", anchorGeoNodeId = "geo_bra", legs = 2, clubCount = 20, exchange = 4 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _baseDepth = await DepthAsync();
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

    private async Task<string> RefusedAsync(string path, object body)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"]!.GetValue<string>();
    }

    private async Task<int> ClubCountAsync() =>
        JsonNode.Parse(await _client.GetStringAsync("/api/clubs"))!.AsArray().Count;

    private async Task<int> DepthAsync() =>
        JsonNode.Parse(await _client.GetStringAsync("/api/history"))!["depth"]!.GetValue<int>();

    private async Task<IReadOnlyList<string>> EnrolledAsync()
    {
        JsonArray countries = JsonNode.Parse(await _client.GetStringAsync("/api/countries"))!.AsArray();
        JsonNode brazil = countries.Single(country => country!["country"]!["countryId"]!.GetValue<string>() == Brazil)!;
        JsonNode level = brazil["levels"]!.AsArray()
            .Single(l => l!["competition"]!["competitionId"]!.GetValue<string>() == Division)!;
        return level["season"]!["participantClubIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToList();
    }

    [Fact]
    public async Task APreview_ListsTheBatch_AndWritesNothing()
    {
        int clubs = await ClubCountAsync();

        JsonNode first = await PostAsync($"{Generate}/preview", Request);
        JsonNode second = await PostAsync($"{Generate}/preview", Request);

        JsonArray rows = first["clubs"]!.AsArray();
        Assert.Equal(4, rows.Count);
        Assert.Equal([0.75, 0.70, 0.65, 0.60], rows.Select(row => row!["strength"]!.GetValue<double>()));
        Assert.All(rows, row =>
        {
            Assert.Equal("B4", row!["band"]!.GetValue<string>());
            Assert.False(string.IsNullOrEmpty(row["cityName"]!.GetValue<string>()));
            Assert.InRange(row["xiOverall"]!.GetValue<int>(), 1, 99);
        });
        Assert.Equal(rows.Sum(row => row!["players"]!.GetValue<int>()), first["players"]!.GetValue<int>());

        // Same request, same world: the same batch.
        Assert.Equal(first.ToJsonString(), second.ToJsonString());

        Assert.Equal(clubs, await ClubCountAsync());
        Assert.Equal(_baseDepth, await DepthAsync());
        Assert.Empty(await EnrolledAsync());
    }

    [Fact]
    public async Task Apply_WritesTheBatch_Enrolled_AsOneUndoableAct()
    {
        int clubs = await ClubCountAsync();
        JsonNode preview = await PostAsync($"{Generate}/preview", Request);

        JsonNode result = await PostAsync($"{Generate}/apply", Request);

        Assert.Equal(4, result["clubs"]!.GetValue<int>());
        Assert.Equal(preview["players"]!.GetValue<int>(), result["players"]!.GetValue<int>());
        Assert.Equal(_baseDepth + 1, result["depth"]!.GetValue<int>());
        Assert.Equal(4, result["pendingEdits"]!.GetValue<int>());
        Assert.Equal(clubs + 4, await ClubCountAsync());
        Assert.Equal(
            preview["clubs"]!.AsArray().Select(row => row!["clubId"]!.GetValue<string>()),
            await EnrolledAsync());

        HttpResponseMessage undo = await _client.PostAsync("/api/undo", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        Assert.Equal(clubs, await ClubCountAsync());
        Assert.Empty(await EnrolledAsync());
        Assert.Equal(_baseDepth, await DepthAsync());
    }

    [Fact]
    public async Task ADivisionThatDoesNotExist_Is400_AndWritesNothing()
    {
        string error = await RefusedAsync($"/api/countries/{Brazil}/divisions/div_bra_9/generate/apply", Request);

        Assert.Contains("div_bra_9", error);
        Assert.Equal(_baseDepth, await DepthAsync());
    }

    [Fact]
    public async Task MoreClubsThanSeats_Is400_AndWritesNothing()
    {
        int clubs = await ClubCountAsync();

        string error = await RefusedAsync(
            $"{Generate}/apply",
            new { clubCount = 21, band = "B4", strengthMin = 0.6, strengthMax = 0.75, seed = 2026 });

        Assert.Contains("20 free seat(s)", error);
        Assert.Equal(clubs, await ClubCountAsync());
        Assert.Equal(_baseDepth, await DepthAsync());
    }

    [Fact]
    public async Task AStrengthRangeOutsideTheScale_Is400_AndWritesNothing()
    {
        string error = await RefusedAsync(
            $"{Generate}/preview",
            new { clubCount = 4, band = "B4", strengthMin = 0.8, strengthMax = 0.6, seed = 2026 });

        Assert.Contains("0 < min ≤ max ≤ 1", error);
        Assert.Equal(_baseDepth, await DepthAsync());
    }
}
