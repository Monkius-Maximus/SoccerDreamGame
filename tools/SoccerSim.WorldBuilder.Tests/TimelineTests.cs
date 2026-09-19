using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The navigable edit history (ROADMAP.md Sprint 9). The undo stack existed but was linear and
/// invisible: you could press it, one step at a time, and never see what you were stepping through.
///
/// <para>Its own fixture per test — every test here changes the world on purpose.</para>
/// </summary>
public sealed class TimelineTests : IAsyncLifetime
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

    private async Task<JsonNode> TimelineAsync(int offset = 0) =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/history/timeline?offset={offset}"))!;

    private static JsonArray Steps(JsonNode timeline) => timeline["steps"]!.AsArray();

    private static JsonArray Edits(JsonNode timeline) => timeline["edits"]!.AsArray();

    private async Task<JsonNode> ClubAsync(string clubId = "clb_bra_rio_001") =>
        JsonNode.Parse(await _client.GetStringAsync($"/api/clubs/{clubId}"))!;

    private async Task PatchAsync(string path, string value)
    {
        HttpResponseMessage response = await _client.PatchAsJsonAsync("/api/clubs/clb_bra_rio_001", new
        {
            path,
            value,
            version = (await ClubAsync())["version"]!.GetValue<long>(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> NicknameAsync() =>
        (await ClubAsync())["club"]!["identity"]!["nickname"]!.GetValue<string>();

    // -------------------------------------------------------------- the shape

    [Fact]
    public async Task AFreshWorld_HasNothingToShow()
    {
        JsonNode timeline = await TimelineAsync();

        Assert.Empty(Steps(timeline));
        Assert.Empty(Edits(timeline));
        Assert.Equal(0, timeline["editTotal"]!.GetValue<int>());
        Assert.Equal(25, timeline["cap"]!.GetValue<int>());
    }

    [Fact]
    public async Task EachActAppearsOnce_NewestFirst_WithHowFarBackItIs()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");
        await PatchAsync("stadium.name", "Arena Terceira");

        JsonArray steps = Steps(await TimelineAsync());

        Assert.Equal(3, steps.Count);
        Assert.Equal(
            ["Editar stadium.name", "Editar identity.nickname", "Editar identity.nickname"],
            steps.Select(step => step!["label"]!.GetValue<string>()));

        // The newest act is one step back, the one below it two: what returning there would cost.
        Assert.Equal([1, 2, 3], steps.Select(step => step!["stepsBack"]!.GetValue<int>()));
    }

    /// <summary>
    /// The trail says what actually changed, which the act's label only names. Both are needed:
    /// "Editar identity.nickname" is the act; "Os Vermelhos do Sul → Alcunha Nova" is the edit.
    /// </summary>
    [Fact]
    public async Task AnEditCarriesTheOldAndNewValue_AndTheNameOfWhatItChanged()
    {
        string before = await NicknameAsync();
        await PatchAsync("identity.nickname", "Alcunha Nova");

        JsonNode edit = Assert.Single(Edits(await TimelineAsync()))!;

        Assert.Equal("Club", edit["entityType"]!.GetValue<string>());
        Assert.Equal("clb_bra_rio_001", edit["entityId"]!.GetValue<string>());
        Assert.Equal("Carioca Sul", edit["entityName"]!.GetValue<string>());
        Assert.Equal("identity.nickname", edit["fieldPath"]!.GetValue<string>());
        Assert.Equal(before, edit["oldValue"]!.GetValue<string>());
        Assert.Equal("Alcunha Nova", edit["newValue"]!.GetValue<string>());
        Assert.False(edit["exported"]!.GetValue<bool>());
    }

    /// <summary>
    /// The link migration 0017 exists for. Without it an edit is a row with no act, and the one
    /// act that writes hundreds of them at once is exactly where that stops being survivable.
    /// </summary>
    [Fact]
    public async Task AnEditNamesTheActItWasPartOf()
    {
        await PatchAsync("identity.nickname", "Alcunha Nova");

        JsonNode timeline = await TimelineAsync();
        JsonNode act = Assert.Single(Steps(timeline))!;
        JsonNode edit = Assert.Single(Edits(timeline))!;

        Assert.Equal(act["id"]!.GetValue<long>(), edit["historyId"]!.GetValue<long>());
        Assert.Equal("Editar identity.nickname", edit["actLabel"]!.GetValue<string>());

        // And the act says how many edits it produced, so the screen can fold them under it.
        Assert.Equal(1, act["edits"]!.GetValue<int>());
    }

    /// <summary>
    /// A CSV import is one act and several hundred edits. This is the case the link was measured
    /// against: a screen that could not fold them would show one import as several hundred
    /// unexplained lines.
    /// </summary>
    [Fact]
    public async Task AnImportIsOneAct_HoweverManyRowsItChanged()
    {
        string clubs = (await _client.GetStringAsync("/api/export/csv/Clubes")).TrimStart('﻿');

        HttpResponseMessage applied = await _client.PostAsJsonAsync("/api/import/apply", new
        {
            files = new[] { new { tab = "Clubes", content = clubs.Replace("Belém", "Belem") } },
        });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        JsonNode timeline = await TimelineAsync();
        JsonNode act = Assert.Single(Steps(timeline))!;

        Assert.StartsWith("Importar CSV", act["label"]!.GetValue<string>());
        Assert.True(act["edits"]!.GetValue<int>() > 0);

        // Every row of the import points back at that one act.
        Assert.All(
            Edits(timeline),
            edit => Assert.Equal(act["id"]!.GetValue<long>(), edit!["historyId"]!.GetValue<long>()));

        // And each row names the CELL that moved, with the two values it moved between. This used
        // to record the row's own label on both sides of the arrow — "Belém → Belém" — which
        // proves an edit happened and says nothing about what it was.
        JsonNode moved = Edits(timeline).First(edit =>
            edit!["fieldPath"]!.GetValue<string>().StartsWith("csv:Clubes.", StringComparison.Ordinal));

        Assert.NotEqual(
            moved["oldValue"]!.GetValue<string>(),
            moved["newValue"]!.GetValue<string>());
        Assert.Equal("Belém", moved["oldValue"]!.GetValue<string>());
        Assert.Equal("Belem", moved["newValue"]!.GetValue<string>());
    }

    // ------------------------------------------------------------ the travelling

    /// <summary>
    /// The claim the whole screen rests on: jumping straight to an act lands on exactly the world
    /// that pressing undo the whole way would. Asserted rather than argued — two worlds built the
    /// same way, one walked back step by step and one jumped, compared field by field.
    /// </summary>
    [Fact]
    public async Task JumpingBackIsTheSameWorldAsSteppingBack()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");
        await PatchAsync("identity.nickname", "Terceiro");
        await PatchAsync("stadium.name", "Arena Quarta");

        JsonArray steps = Steps(await TimelineAsync());
        long target = steps[3]!["id"]!.GetValue<long>();

        // One world: step back four times.
        for (int i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/undo", null)).StatusCode);

        string stepped = (await ClubAsync())["club"]!.ToJsonString();
        Assert.Equal(0, Steps(await TimelineAsync()).Count);

        // The other: the same four edits, then one jump.
        using var second = new WorldBuilderApp();
        HttpClient client = second.CreateClient();

        foreach (string nickname in new[] { "Primeiro", "Segundo", "Terceiro" })
        {
            JsonNode club = JsonNode.Parse(await client.GetStringAsync("/api/clubs/clb_bra_rio_001"))!;
            await client.PatchAsJsonAsync("/api/clubs/clb_bra_rio_001", new
            {
                path = "identity.nickname",
                value = nickname,
                version = club["version"]!.GetValue<long>(),
            });
        }

        JsonNode last = JsonNode.Parse(await client.GetStringAsync("/api/clubs/clb_bra_rio_001"))!;
        await client.PatchAsJsonAsync("/api/clubs/clb_bra_rio_001", new
        {
            path = "stadium.name",
            value = "Arena Quarta",
            version = last["version"]!.GetValue<long>(),
        });

        JsonArray otherSteps = JsonNode.Parse(
            await client.GetStringAsync("/api/history/timeline"))!["steps"]!.AsArray();

        HttpResponseMessage reverted = await client.PostAsync(
            $"/api/history/revert/{otherSteps[3]!["id"]!.GetValue<long>()}", null);
        Assert.Equal(HttpStatusCode.OK, reverted.StatusCode);

        string jumped = JsonNode.Parse(
            await client.GetStringAsync("/api/clubs/clb_bra_rio_001"))!["club"]!.ToJsonString();

        Assert.Equal(stepped, jumped);

        // The ids differ between the two databases, but the target was the same act.
        Assert.Equal(steps[3]!["label"]!.GetValue<string>(), otherSteps[3]!["label"]!.GetValue<string>());
        Assert.NotEqual(0, target);
    }

    [Fact]
    public async Task RevertingSaysWhatItUndidAndHowMuchItThrewAway()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");
        await PatchAsync("identity.nickname", "Terceiro");

        JsonArray steps = Steps(await TimelineAsync());

        // Back to the second act: it and the one after it go.
        HttpResponseMessage response = await _client.PostAsync(
            $"/api/history/revert/{steps[1]!["id"]!.GetValue<long>()}", null);

        JsonNode result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Editar identity.nickname", result["label"]!.GetValue<string>());
        Assert.Equal(2, result["discarded"]!.GetValue<int>());
        Assert.Equal(1, result["depth"]!.GetValue<int>());

        // The world is as it was before the second edit.
        Assert.Equal("Primeiro", await NicknameAsync());
    }

    /// <summary>The stack runs one way. Returning to a point throws away what came after, and the
    /// discarded acts do not come back.</summary>
    [Fact]
    public async Task TheActsAfterThePointAreGone_ThereIsNoRedo()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");
        await PatchAsync("identity.nickname", "Terceiro");

        JsonArray steps = Steps(await TimelineAsync());
        long oldest = steps[2]!["id"]!.GetValue<long>();

        await _client.PostAsync($"/api/history/revert/{oldest}", null);

        Assert.Empty(Steps(await TimelineAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/undo", null)).StatusCode);

        // Returning to a point that is gone is refused with a reason, not a 500.
        HttpResponseMessage again = await _client.PostAsync($"/api/history/revert/{oldest}", null);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains(
            "não está mais na pilha",
            JsonNode.Parse(await again.Content.ReadAsStringAsync())!["error"]!.GetValue<string>());
    }

    /// <summary>
    /// The trail is not the stack. Walking the world back does not rewrite what was recorded —
    /// the edits stay, because they are a provenance trail and a trail that edits itself is not
    /// evidence of anything.
    /// </summary>
    [Fact]
    public async Task RevertingDoesNotEraseTheTrail()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");

        int recorded = (await TimelineAsync())["editTotal"]!.GetValue<int>();
        Assert.Equal(2, recorded);

        await _client.PostAsync(
            $"/api/history/revert/{Steps(await TimelineAsync())[1]!["id"]!.GetValue<long>()}", null);

        JsonNode timeline = await TimelineAsync();
        Assert.Equal(recorded, timeline["editTotal"]!.GetValue<int>());
        Assert.Empty(Steps(timeline));

        // The acts are gone, so the rows that named them no longer resolve to a label — and say so
        // rather than pointing at nothing.
        Assert.All(Edits(timeline), edit => Assert.Null(edit!["actLabel"]));
    }

    // ------------------------------------------------------------------ paging

    /// <summary>
    /// The trail is append-only and unbounded, so the screen pages it. The stack never needs
    /// paging: it is capped at 25 by construction.
    /// </summary>
    [Fact]
    public async Task TheTrailIsPaged_AndAPageIsAWindowOntoIt()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");
        await PatchAsync("identity.nickname", "Terceiro");

        JsonNode first = await TimelineAsync();

        Assert.Equal(3, first["editTotal"]!.GetValue<int>());
        Assert.Equal(50, first["pageSize"]!.GetValue<int>());
        Assert.Equal(0, first["editOffset"]!.GetValue<int>());
        Assert.Equal(
            ["Terceiro", "Segundo", "Primeiro"],
            Edits(first).Select(edit => edit!["newValue"]!.GetValue<string>()));

        // An offset skips from the newest end, so the window moves down the trail rather than
        // starting it again.
        JsonNode second = await TimelineAsync(offset: 1);

        Assert.Equal(1, second["editOffset"]!.GetValue<int>());
        Assert.Equal(3, second["editTotal"]!.GetValue<int>());
        Assert.Equal(
            ["Segundo", "Primeiro"],
            Edits(second).Select(edit => edit!["newValue"]!.GetValue<string>()));

        // Past the end is an empty page, not an error: the total is what says there is no more.
        Assert.Empty(Edits(await TimelineAsync(offset: 99)));
    }

    /// <summary>
    /// An act's edit count is a fact about the act, not about the page being read. Counted over
    /// the page it would shrink to zero as the reader scrolled away from the act's own rows.
    /// </summary>
    [Fact]
    public async Task AnActKeepsItsEditCount_EvenOnAPageThatShowsNoneOfItsEdits()
    {
        await PatchAsync("identity.nickname", "Primeiro");
        await PatchAsync("identity.nickname", "Segundo");
        await PatchAsync("identity.nickname", "Terceiro");

        // A page past every row the newest act wrote.
        JsonNode timeline = await TimelineAsync(offset: 2);

        Assert.Single(Edits(timeline));
        Assert.All(Steps(timeline), step => Assert.Equal(1, step!["edits"]!.GetValue<int>()));
    }

    /// <summary>The stack never needs paging — it is capped at 25 by construction, and the screen
    /// draws all of it.</summary>
    [Fact]
    public async Task TheStackNeverGrowsPastItsCap()
    {
        for (int i = 0; i < 30; i++)
            await PatchAsync("identity.nickname", $"Alcunha {i}");

        JsonNode timeline = await TimelineAsync();

        Assert.Equal(25, Steps(timeline).Count);
        Assert.Equal(25, Steps(timeline)[^1]!["stepsBack"]!.GetValue<int>());

        // The trail kept all thirty: it is append-only, and the cap is the stack's rule, not its.
        Assert.Equal(30, timeline["editTotal"]!.GetValue<int>());

        // The five whose act was trimmed away say so instead of naming one that is gone.
        JsonNode oldest = Assert.Single(Edits(await TimelineAsync(offset: 29)))!;
        Assert.Null(oldest["actLabel"]);
    }

    [Fact]
    public async Task ANegativeOffsetReadsAsTheFirstPage()
    {
        await PatchAsync("identity.nickname", "Uma alcunha");

        Assert.Equal(0, (await TimelineAsync(offset: -20))["editOffset"]!.GetValue<int>());
    }
}
