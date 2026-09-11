using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// Sprint 8 over HTTP: the sweep, the gate, and the report that leaves the tool.
/// </summary>
public sealed class AuditApiTests : IClassFixture<WorldBuilderApp>
{
    private readonly HttpClient _client;

    public AuditApiTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> AuditAsync() => JsonNode.Parse(await _client.GetStringAsync("/api/audit"))!;

    [Fact]
    public async Task TheSweep_ReportsTheKnownGolden()
    {
        JsonNode audit = await AuditAsync();

        Assert.Equal(1, audit["errors"]!.GetValue<int>());

        // Five, not the four the club sweep alone finds: over HTTP the audit also sees the
        // country profiles, and Brazil's nationality mix was measured from the batch it will go
        // on to generate — which is not evidence about the world, and says so.
        Assert.Equal(5, audit["warnings"]!.GetValue<int>());
        Assert.Equal(5, audit["clubsAffected"]!.GetValue<int>());
        Assert.Equal(20, audit["clubsScanned"]!.GetValue<int>());
        Assert.Equal(688, audit["playersScanned"]!.GetValue<int>());
        Assert.Equal(27, audit["sourcesRegistered"]!.GetValue<int>());
        Assert.False(audit["released"]!.GetValue<bool>());
    }

    [Fact]
    public async Task FindingsComeGroupedByCode_ErrorsFirst()
    {
        JsonArray groups = (await AuditAsync())["groups"]!.AsArray();

        Assert.Equal(3, groups.Count);

        // "What is blocking the batch" before "what else is there", then the biggest group.
        Assert.Equal("PHONETIC_WINDOW", groups[0]!["code"]!.GetValue<string>());
        Assert.Equal("Error", groups[0]!["level"]!.GetValue<string>());
        Assert.Equal("DERBY_ONE_WAY", groups[1]!["code"]!.GetValue<string>());
        Assert.Equal(4, groups[1]!["count"]!.GetValue<int>());
        Assert.Equal("NATIONALITY_UNSOURCED", groups[2]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task EveryFinding_CarriesTheClubItIsAbout()
    {
        // This is what makes a row clickable: the sweep is a place to start fixing, not a list.
        // A club finding points at a club; a world finding (a country, a pyramid) points at what
        // it is about and the screen leaves it unclickable rather than jumping somewhere wrong.
        foreach (JsonNode? group in (await AuditAsync())["groups"]!.AsArray())
        {
            foreach (JsonNode? finding in group!["findings"]!.AsArray())
            {
                string scope = finding!["scope"]!.GetValue<string>();
                Assert.Contains(scope, new[] { "Club", "World" });
                Assert.NotEmpty(finding["entityId"]!.GetValue<string>());
                Assert.NotEmpty(finding["entityLabel"]!.GetValue<string>());

                if (scope == "Club")
                    Assert.StartsWith("clb_", finding["entityId"]!.GetValue<string>());
            }
        }
    }

    [Fact]
    public async Task SourcesComeGroupedByTheme_WithTheUrllessOnesApart()
    {
        JsonNode audit = await AuditAsync();

        JsonArray groups = audit["sources"]!.AsArray();
        Assert.NotEmpty(groups);
        Assert.All(groups, group => Assert.All(
            group!["sources"]!.AsArray(),
            source => Assert.NotNull(source!["url"])));

        // The one prose cross-reference: a note, not a broken link, and never an empty anchor.
        JsonNode note = Assert.Single(audit["notes"]!.AsArray())!;
        Assert.Null(note["url"]);
        Assert.NotEmpty(note["tema"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheReport_IsMarkdownCarryingBothFindingsAndSources()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/audit/report");
        string markdown = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("auditoria_base_de_mundo.md", response.Content.Headers.ContentDisposition!.ToString());

        Assert.Contains("# Auditoria da base de mundo", markdown);
        Assert.Contains("**Lote bloqueado.**", markdown);
        Assert.Contains("`PHONETIC_WINDOW`", markdown);
        Assert.Contains("`DERBY_ONE_WAY`", markdown);

        // The evidence and the exceptions belong in the same document.
        Assert.Contains("## Fontes", markdown);
        Assert.Contains("### Remissões sem URL", markdown);
    }

    [Fact]
    public async Task TheAuditScreen_IsReachableFromTheShell()
    {
        string html = await _client.GetStringAsync("/");
        string script = await _client.GetStringAsync("/app.js");

        Assert.Contains("data-view=\"auditoria\"", html);
        Assert.Contains("/api/audit", script);
    }
}
