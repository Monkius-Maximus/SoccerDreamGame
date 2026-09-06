using System.Net;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The front end is served and wired to the endpoints it calls. Deliberately shallow: the screens
/// are still changing, and a test that asserts on markup would break on every design pass without
/// catching anything a person looking at the page would miss (ROADMAP.md Sprint 3).
/// </summary>
public sealed class FrontEndSmokeTests : IClassFixture<WorldBuilderApp>
{
    private readonly HttpClient _client;

    public FrontEndSmokeTests(WorldBuilderApp app) => _client = app.CreateClient();

    [Fact]
    public async Task Root_ServesTheShell()
    {
        HttpResponseMessage response = await _client.GetAsync("/");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("Terra Paralela", html);

        // The three anchors app.js renders into. If one is renamed here, the page silently stops
        // filling in — which is exactly the failure this shallow test is for.
        Assert.Contains("id=\"rail-list\"", html);
        Assert.Contains("id=\"content\"", html);
        Assert.Contains("id=\"loading\"", html);
    }

    [Theory]
    [InlineData("/app.css", "text/css")]
    [InlineData("/app.js", "javascript")]
    public async Task Assets_AreServed(string path, string expectedContentType)
    {
        HttpResponseMessage response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedContentType, response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Script_CallsOnlyEndpointsThatExist()
    {
        string script = await _client.GetStringAsync("/app.js");

        foreach (string url in new[] { "/api/world", "/api/clubs" })
        {
            Assert.Contains(url, script);
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(url)).StatusCode);
        }
    }
}

/// <summary>The empty database is a first-run state, not a failure: the tool explains itself and
/// says how to load a world (the user asked for this explicitly during design).</summary>
public sealed class EmptyWorldTests
{
    [Fact]
    public async Task WithNoWorldImported_TheApiSaysSoInsteadOfFailing()
    {
        using var app = WorldBuilderApp.Empty();
        HttpClient client = app.CreateClient();

        string world = await client.GetStringAsync("/api/world");
        Assert.Contains("\"loaded\":false", world);

        HttpResponseMessage clubs = await client.GetAsync("/api/clubs");
        Assert.Equal(HttpStatusCode.OK, clubs.StatusCode);
        Assert.Equal("[]", await clubs.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheShellStillLoads_AndTheScriptCarriesTheFirstRunInstructions()
    {
        using var app = WorldBuilderApp.Empty();
        HttpClient client = app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Contains("worldbuilder import", await client.GetStringAsync("/app.js"));
    }
}
