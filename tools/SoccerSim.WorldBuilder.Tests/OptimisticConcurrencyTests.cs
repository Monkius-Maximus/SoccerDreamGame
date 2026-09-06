using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Two edits made against the same version: the first wins, the second is told rather than
/// silently losing. For a tool whose whole claim is being the source of truth, quietly discarding
/// somebody's work is the one failure that cannot be tolerated.
/// </summary>
public sealed class OptimisticConcurrencyTests : IClassFixture<WorldBuilderApp>
{
    private const string ClubId = "clb_bra_sao_001";
    private const string PlayerId = "plr_sao_001_0041";

    private readonly HttpClient _client;

    public OptimisticConcurrencyTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<long> ClubVersionAsync() =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{ClubId}"))!["version"]!.GetValue<long>();

    [Fact]
    public async Task TheSecondPatchAgainstAStaleVersion_Gets409()
    {
        long version = await ClubVersionAsync();

        HttpResponseMessage first = await _client.PatchAsJsonAsync(
            $"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Primeiro", version });
        HttpResponseMessage second = await _client.PatchAsJsonAsync(
            $"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Segundo", version });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // The first edit stands; the conflict did not overwrite it.
        JsonNode club = JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{ClubId}"))!["club"]!;
        Assert.Equal("Primeiro", club["crest"]!["motto"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheConflictSaysWhichVersionsDisagreed()
    {
        long stale = await ClubVersionAsync();
        await _client.PatchAsJsonAsync($"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Avança", version = stale });

        HttpResponseMessage conflict = await _client.PatchAsJsonAsync(
            $"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Atrasado", version = stale });
        JsonNode body = JsonNode.Parse(await conflict.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(stale, body["expectedVersion"]!.GetValue<long>());
        Assert.Equal(stale + 1, body["actualVersion"]!.GetValue<long>());
    }

    [Fact]
    public async Task RereadingAfterAConflict_LetsTheEditThrough()
    {
        // The recovery a client actually performs: read again, then re-apply.
        long stale = await ClubVersionAsync();
        await _client.PatchAsJsonAsync($"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Um", version = stale });

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await _client.PatchAsJsonAsync($"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Dois", version = stale })).StatusCode);

        HttpResponseMessage retry = await _client.PatchAsJsonAsync(
            $"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Dois", version = await ClubVersionAsync() });

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task AConflictLeavesThePendingCounterAlone()
    {
        long stale = await ClubVersionAsync();
        await _client.PatchAsJsonAsync($"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Contado", version = stale });

        int pending = await _client.GetFromJsonAsync<int>("/api/pending");
        await _client.PatchAsJsonAsync($"/api/clubs/{ClubId}", new { path = "crest.motto", value = "Perdido", version = stale });

        Assert.Equal(pending, await _client.GetFromJsonAsync<int>("/api/pending"));
    }

    [Fact]
    public async Task CharactersAreVersionedTheSameWay()
    {
        JsonNode page = JsonNode.Parse(await _client.GetStringAsync($"/api/characters/{PlayerId}"))!;
        long version = page["version"]!.GetValue<long>();

        HttpResponseMessage first = await _client.PatchAsJsonAsync(
            $"/api/characters/{PlayerId}", new { path = "attrs.Passing", value = "80", version });
        HttpResponseMessage second = await _client.PatchAsJsonAsync(
            $"/api/characters/{PlayerId}", new { path = "attrs.Passing", value = "40", version });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        JsonNode after = JsonNode.Parse(await _client.GetStringAsync($"/api/characters/{PlayerId}"))!;
        Assert.Equal(80, after["character"]!["attrs"]!["Passing"]!.GetValue<int>());
    }
}
