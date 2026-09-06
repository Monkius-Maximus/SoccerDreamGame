using System.Text.Json.Nodes;
using Xunit;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The form is generated from /api/fields and its select options from /api/world's enums. These
/// two endpoints therefore have to agree, and nothing in the type system makes them: a field can
/// name an enum the world summary does not publish, and the result is an empty dropdown that
/// silently cannot be set. That is exactly the bug this pins.
/// </summary>
public sealed class FieldCatalogApiTests : IClassFixture<WorldBuilderApp>
{
    private readonly HttpClient _client;

    public FieldCatalogApiTests(WorldBuilderApp app) => _client = app.CreateClient();

    private async Task<JsonNode> GetAsync(string url) =>
        JsonNode.Parse(await _client.GetStringAsync(url))!;

    [Fact]
    public async Task EveryEnumFieldNamesAnEnumTheWorldSummaryPublishes()
    {
        JsonNode catalog = await GetAsync("/api/fields");
        JsonObject enums = (await GetAsync("/api/world"))["enums"]!.AsObject();

        var missing = new List<string>();
        foreach (string side in new[] { "club", "character" })
        {
            foreach (JsonNode? group in catalog[side]!.AsArray())
            {
                foreach (JsonNode? field in group!["fields"]!.AsArray())
                {
                    if (field!["kind"]!.GetValue<string>() != "Enum")
                        continue;

                    string enumName = field["enumName"]!.GetValue<string>();
                    if (!enums.ContainsKey(enumName))
                        missing.Add($"{field["path"]!.GetValue<string>()} → {enumName}");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task TheCatalogDescribesTheNineClubGroups()
    {
        JsonNode catalog = await GetAsync("/api/fields");

        Assert.Equal(
            new[] { "identity", "geography", "world", "crest", "palette", "kits", "stadium", "aiProfile", "audit" },
            catalog["club"]!.AsArray().Select(group => group!["id"]!.GetValue<string>()));
    }

    [Fact]
    public async Task EveryFieldCarriesASealAndAnEditableFlag()
    {
        JsonNode catalog = await GetAsync("/api/fields");
        string[] seals = ["Authored", "Derived", "Sampled", "Calculated"];

        foreach (string side in new[] { "club", "character" })
        {
            foreach (JsonNode? group in catalog[side]!.AsArray())
            {
                foreach (JsonNode? field in group!["fields"]!.AsArray())
                {
                    Assert.Contains(field!["provenance"]!.GetValue<string>(), seals);
                    Assert.NotNull(field["editable"]);
                    Assert.False(string.IsNullOrWhiteSpace(field["label"]!.GetValue<string>()));
                }
            }
        }
    }

    [Fact]
    public async Task TheAttributeGroupCarriesTheTwelveAttributes()
    {
        JsonNode catalog = await GetAsync("/api/fields");

        JsonNode attrs = catalog["character"]!.AsArray()
            .Single(group => group!["id"]!.GetValue<string>() == "attrs")!;

        Assert.Equal(12, attrs["fields"]!.AsArray().Count);
    }
}
