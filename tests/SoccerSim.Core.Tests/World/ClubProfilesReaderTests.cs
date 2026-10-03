using System.Text.Json.Nodes;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Serialization;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The club profiles are data, so the reader is where "no number without a source" and "no
/// silent default" are enforced for generated clubs (ADR-0011 §3).
/// </summary>
public sealed class ClubProfilesReaderTests
{
    private static readonly string Json =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "club_profiles.json"));

    private static string Edit(Action<JsonObject> change)
    {
        JsonObject root = JsonNode.Parse(Json)!.AsObject();
        change(root);
        return root.ToJsonString();
    }

    [Fact]
    public void TheBrazilProfiles_Read()
    {
        ClubProfiles profiles = ClubProfilesReader.Read(Json);

        Assert.Equal("BRA", profiles.CountryId);
        Assert.Equal(11, profiles.Cities.Count);
        Assert.Equal(Enum.GetValues<DistrictArchetype>().Length, profiles.Qualifiers.Count);
        Assert.Equal(Enum.GetValues<PrestigeBand>().Length, profiles.SquadSizeByBand.Count);
        Assert.Equal(500, profiles.CapacityStep);
    }

    [Fact]
    public void EverySection_NamesADeclaredSource()
    {
        ClubProfiles profiles = ClubProfilesReader.Read(Json);

        Assert.Equal(23, profiles.SectionSources.Count);
        Assert.All(profiles.SectionSources.Values.SelectMany(keys => keys), key => Assert.Contains(key, profiles.Sources.Keys));
    }

    [Fact]
    public void ASectionWithoutASource_IsRejectedByPath()
    {
        string json = Edit(root => root["mottos"]!.AsObject().Remove("source"));

        var ex = Assert.Throws<WorldImportException>(() => ClubProfilesReader.Read(json));

        Assert.Contains(ex.Errors, error => error.Contains("$.mottos.source"));
    }

    [Fact]
    public void ASourceThatIsNotDeclared_IsRejected()
    {
        string json = Edit(root => root["mottos"]!["source"] = new JsonArray("folklore"));

        var ex = Assert.Throws<WorldImportException>(() => ClubProfilesReader.Read(json));

        Assert.Contains(ex.Errors, error => error.Contains("'folklore' is not declared"));
    }

    [Fact]
    public void AnEnumTheSchemaDoesNotKnow_IsRejectedNotDefaulted()
    {
        string json = Edit(root => root["shieldShapes"]!["weights"]!["Hexagon"] = 3);

        var ex = Assert.Throws<WorldImportException>(() => ClubProfilesReader.Read(json));

        Assert.Contains(ex.Errors, error => error.Contains("'Hexagon' is not a valid ShieldShape"));
    }

    [Fact]
    public void APrimaryColourWithoutANickname_IsRejected()
    {
        string json = Edit(root =>
        {
            JsonArray items = root["nicknames"]!["items"]!.AsArray();
            JsonNode azul = items.First(item => item!["text"]!.GetValue<string>() == "Os Azulões")!;
            items.Remove(azul);
        });

        var ex = Assert.Throws<WorldImportException>(() => ClubProfilesReader.Read(json));

        Assert.Contains(ex.Errors, error => error.Contains("'azul' can be a primary but has no single-family nickname"));
    }

    [Fact]
    public void ADistrictWithoutQualifiers_IsRejected()
    {
        string json = Edit(root => root["qualifiers"]!["values"]!.AsObject().Remove("Coastal"));

        var ex = Assert.Throws<WorldImportException>(() => ClubProfilesReader.Read(json));

        Assert.Contains(ex.Errors, error => error.Contains("$.qualifiers.values.Coastal"));
    }

    [Fact]
    public void ASquadSizeOutsideTheEditorsRange_IsRejected()
    {
        string json = Edit(root => root["squadSizeByBand"]!["values"]!["B6"] = 22);

        var ex = Assert.Throws<WorldImportException>(() => ClubProfilesReader.Read(json));

        Assert.Contains(ex.Errors, error => error.Contains("22 is outside 28–40"));
    }

    [Fact]
    public void EveryProblem_IsReportedInOnePass()
    {
        string json = Edit(root =>
        {
            root["mottos"]!.AsObject().Remove("source");
            root["shieldShapes"]!["weights"]!["Hexagon"] = 3;
        });

        var ex = Assert.Throws<WorldImportException>(() => ClubProfilesReader.Read(json));

        Assert.Equal(2, ex.Errors.Count);
    }
}
