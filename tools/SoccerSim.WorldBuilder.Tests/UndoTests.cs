using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Sprint 9's undo stack. The tool writes immediately and has no Save button, so this is what
/// makes that a safe design rather than a bet: do it, look, undo.
///
/// <para>Its own fixture, because every test here changes the world on purpose.</para>
/// </summary>
public sealed class UndoTests : IAsyncLifetime
{
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

    private async Task<JsonNode> HistoryAsync() => JsonNode.Parse(await _client.GetStringAsync("/api/history"))!;

    private async Task<int> DepthAsync() => (await HistoryAsync())["depth"]!.GetValue<int>();

    private async Task<JsonNode> ClubAsync(string clubId = "clb_bra_rio_001") =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{clubId}"))!;

    private async Task<HttpResponseMessage> PatchAsync(string path, string value, string clubId = "clb_bra_rio_001") =>
        await _client.PatchAsJsonAsync($"/api/clubs/{clubId}", new
        {
            path,
            value,
            version = (await ClubAsync(clubId))["version"]!.GetValue<long>(),
        });

    [Fact]
    public async Task AFreshDatabase_HasNothingToUndo()
    {
        JsonNode history = await HistoryAsync();

        Assert.Equal(0, history["depth"]!.GetValue<int>());
        Assert.Null(history["nextLabel"]);
        Assert.Equal(25, history["cap"]!.GetValue<int>());

        HttpResponseMessage response = await _client.PostAsync("/api/undo", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AFieldEdit_CanBeUndone()
    {
        string before = (await ClubAsync())["club"]!["identity"]!["nickname"]!.GetValue<string>();

        await PatchAsync("identity.nickname", "Alcunha Trocada");
        Assert.Equal("Alcunha Trocada", (await ClubAsync())["club"]!["identity"]!["nickname"]!.GetValue<string>());
        Assert.Equal(1, await DepthAsync());

        HttpResponseMessage undo = await _client.PostAsync("/api/undo", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        Assert.Equal(before, (await ClubAsync())["club"]!["identity"]!["nickname"]!.GetValue<string>());
        Assert.Equal(0, await DepthAsync());
    }

    [Fact]
    public async Task TheLabelSaysWhatWouldComeBack()
    {
        await PatchAsync("identity.nickname", "Uma alcunha");

        Assert.Equal("Editar identity.nickname", (await HistoryAsync())["nextLabel"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheStackIsAStack_NewestFirst()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");
        await PatchAsync("stadium.name", "Arena Terceira");

        Assert.Equal(3, await DepthAsync());
        Assert.Equal("Editar stadium.name", (await HistoryAsync())["nextLabel"]!.GetValue<string>());

        // One press takes back the stadium, leaving the nickname at "Segundo".
        await _client.PostAsync("/api/undo", null);
        Assert.Equal("Segundo", (await ClubAsync())["club"]!["identity"]!["nickname"]!.GetValue<string>());

        await _client.PostAsync("/api/undo", null);
        Assert.Equal("Primeiro", (await ClubAsync())["club"]!["identity"]!["nickname"]!.GetValue<string>());
    }

    /// <summary>
    /// The reason the stack exists. Regenerating a squad throws away every anchored player in it,
    /// and a confirm() asks a question nobody can answer without seeing the result.
    /// </summary>
    [Fact]
    public async Task RegeneratingASquad_CanBeUndone()
    {
        JsonNode before = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!;
        int anchoredBefore = before.AsArray().Count(player => player!["provenance"]!.GetValue<string>() == "Anchored");

        Assert.True(anchoredBefore > 0);

        await _client.PostAsJsonAsync("/api/clubs/clb_bra_rio_001/squad/generate", new
        {
            squadSize = 30,
            formation = "4-3-3",
            targetOverall = 70,
            ageProfile = "Balanced",
            seed = 5,
        });

        JsonArray generated = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray();
        Assert.Equal(30, generated.Count);
        Assert.DoesNotContain(generated, player => player!["provenance"]!.GetValue<string>() == "Anchored");

        Assert.Equal("Gerar elenco de Carioca Sul", (await HistoryAsync())["nextLabel"]!.GetValue<string>());
        await _client.PostAsync("/api/undo", null);

        JsonArray restored = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray();
        Assert.Equal(before.AsArray().Count, restored.Count);
        Assert.Equal(
            anchoredBefore,
            restored.Count(player => player!["provenance"]!.GetValue<string>() == "Anchored"));
    }

    // ------------------------------------------------------------- deletions

    [Fact]
    public async Task DeletingAClub_TakesItsSquadAndClearsTheRivalsThatNamedIt()
    {
        // Carioca Sul is named as a rival by three other clubs; deleting it without clearing them
        // would leave three DERBY_DANGLING errors in the sweep.
        JsonNode sweepBefore = JsonNode.Parse(await _client.GetStringAsync("/api/audit"))!;

        HttpResponseMessage response = await _client.DeleteAsync("/api/clubs/clb_bra_rio_001");
        JsonNode result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Carioca Sul", result["clubName"]!.GetValue<string>());
        Assert.True(result["players"]!.GetValue<int>() > 0);
        Assert.Equal(3, result["rivalsCleared"]!.GetValue<int>());
        Assert.Equal(1, result["competitionsLeft"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/clubs/clb_bra_rio_001")).StatusCode);
        Assert.Equal(19, JsonNode.Parse(await _client.GetStringAsync("/api/clubs"))!.AsArray().Count);

        JsonNode sweepAfter = JsonNode.Parse(await _client.GetStringAsync("/api/audit"))!;
        Assert.DoesNotContain(
            sweepAfter["groups"]!.AsArray(),
            group => group!["code"]!.GetValue<string>() == "DERBY_DANGLING");

        // And the whole thing comes back in one press.
        await _client.PostAsync("/api/undo", null);
        Assert.Equal(20, JsonNode.Parse(await _client.GetStringAsync("/api/clubs"))!.AsArray().Count);
        Assert.Equal(
            sweepBefore["errors"]!.GetValue<int>(),
            JsonNode.Parse(await _client.GetStringAsync("/api/audit"))!["errors"]!.GetValue<int>());
    }

    [Fact]
    public async Task DeletingAPlayer_CanBeUndone()
    {
        JsonArray squad = JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray();
        string playerId = squad[0]!["playerId"]!.GetValue<string>();

        await _client.DeleteAsync($"/api/characters/{playerId}");

        Assert.Equal(
            squad.Count - 1,
            JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray().Count);

        await _client.PostAsync("/api/undo", null);

        Assert.Equal(
            squad.Count,
            JsonNode.Parse(await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray().Count);
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotThere_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/clubs/clb_nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/characters/plr_nope")).StatusCode);
        Assert.Equal(0, await DepthAsync());
    }

    // --------------------------------------------------- every write is undoable

    /// <summary>
    /// The guard against forgetting. Every endpoint that changes the world has to push a snapshot
    /// first; this walks them all and fails if one does not, which is the only way a new write
    /// path cannot quietly arrive without undo.
    /// </summary>
    [Theory]
    [InlineData("patch-club")]
    [InlineData("patch-character")]
    [InlineData("generate-squad")]
    [InlineData("import-csv")]
    [InlineData("geo-rename")]
    [InlineData("recalculate-batch")]
    [InlineData("delete-club")]
    [InlineData("delete-player")]
    public async Task EveryWritingEndpoint_PushesASnapshotFirst(string write)
    {
        int before = await DepthAsync();

        await PerformAsync(write);

        Assert.True(
            await DepthAsync() > before,
            $"'{write}' changed the world without pushing an undo snapshot.");
    }

    private async Task PerformAsync(string write)
    {
        switch (write)
        {
            case "patch-club":
                await PatchAsync("identity.nickname", "Alcunha");
                break;

            case "patch-character":
                JsonArray squad = JsonNode.Parse(
                    await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray();
                string playerId = squad[0]!["playerId"]!.GetValue<string>();
                JsonNode player = JsonNode.Parse(await _client.GetStringAsync($"/api/characters/{playerId}"))!;

                await _client.PatchAsJsonAsync($"/api/characters/{playerId}", new
                {
                    path = "firstName",
                    value = "Renomeado",
                    version = player["version"]!.GetValue<long>(),
                });
                break;

            case "generate-squad":
                await _client.PostAsJsonAsync("/api/clubs/clb_bra_rio_001/squad/generate", new
                {
                    squadSize = 30,
                    formation = "4-3-3",
                    targetOverall = 70,
                    ageProfile = "Balanced",
                    seed = 9,
                });
                break;

            case "import-csv":
                string clubs = (await _client.GetStringAsync("/api/export/csv/Clubes")).TrimStart('﻿');
                await _client.PostAsJsonAsync("/api/import/apply", new
                {
                    files = new[] { new { tab = "Clubes", content = clubs.Replace("Belém", "Belem") } },
                });
                break;

            case "geo-rename":
                await _client.PostAsJsonAsync("/api/geo/geo_bra/rename", new { displayName = "Brasil " });
                break;

            case "recalculate-batch":
                await _client.PostAsync("/api/calibration/recalculate", null);
                break;

            case "delete-club":
                await _client.DeleteAsync("/api/clubs/clb_bra_cha_001");
                break;

            case "delete-player":
                JsonArray roster = JsonNode.Parse(
                    await _client.GetStringAsync("/api/clubs/clb_bra_rio_001/squad"))!.AsArray();
                await _client.DeleteAsync($"/api/characters/{roster[0]!["playerId"]!.GetValue<string>()}");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(write), write, "unknown write");
        }
    }
}
