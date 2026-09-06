using System.Text.Json.Nodes;
using SoccerSim.Core.World;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// Loads the design handoff's real dataset (20 clubs, 688 players, extracted from
/// TerraParalela_Liga_Brasileira_BaseDeMundo.xlsx) for the Sprint 1 domain tests to run against.
/// This is deliberately test-only, loose JsonNode traversal — NOT the Sprint 2 importer. The
/// importer owns real parsing/normalization (e.g. "Rotação" → SquadRole.Rotacao); these tests
/// only touch the ASCII-safe fields the color/economy formulas need.
/// </summary>
internal static class WorldFixture
{
    private static readonly Lazy<string> CachedJson = new(
        () => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "world.json")));

    private static readonly Lazy<JsonObject> Cached = new(() => JsonNode.Parse(Json)!.AsObject());

    /// <summary>The raw document, for tests that exercise the importer end to end.</summary>
    public static string Json => CachedJson.Value;

    public static JsonObject Data => Cached.Value;

    public static WorldCalibration BuildCalibration()
    {
        var calibrationNode = Data["calibration"]!.AsObject();

        var constants = new Dictionary<string, CalibrationConstant>();
        foreach (var (key, node) in calibrationNode["constants"]!.AsObject())
        {
            var obj = node!.AsObject();
            constants[key] = new CalibrationConstant(
                obj["value"]!.GetValue<double>(),
                obj["unit"]!.GetValue<string>(),
                obj["note"]!.GetValue<string>());
        }

        var ageMult = calibrationNode["ageMult"]!.AsArray()
            .Select(pair =>
            {
                var arr = pair!.AsArray();
                return new AgeMultStep(arr[0]!.GetValue<int>(), arr[1]!.GetValue<double>());
            })
            .OrderBy(step => step.Age)
            .ToList();

        var bands = new Dictionary<PrestigeBand, PrestigeBandCalibration>();
        foreach (var (key, node) in calibrationNode["bands"]!.AsObject())
        {
            var obj = node!.AsObject();
            bands[Enum.Parse<PrestigeBand>(key)] = new PrestigeBandCalibration(
                obj["valueMult"]!.GetValue<double>(),
                obj["capMean"]!.GetValue<double>(),
                obj["capSd"]!.GetValue<double>(),
                obj["n"]!.GetValue<int>());
        }

        var homeAdv = new Dictionary<AtmosphereArchetype, double>();
        foreach (var (key, node) in calibrationNode["homeAdv"]!.AsObject())
            homeAdv[Enum.Parse<AtmosphereArchetype>(key)] = node!.GetValue<double>();

        var stadiumProfile = new Dictionary<string, StadiumProfileEntry>();
        foreach (var (key, node) in calibrationNode["stadiumProfile"]!.AsObject())
        {
            var obj = node!.AsObject();
            stadiumProfile[key] = new StadiumProfileEntry(
                obj["mean"]!.GetValue<double>(),
                obj["sd"]!.GetValue<double>(),
                obj["min"]!.GetValue<double>(),
                obj["max"]!.GetValue<double>());
        }

        var positionWeights = new Dictionary<Position, IReadOnlyDictionary<Attr, double>>();
        foreach (var (posKey, node) in Data["positionWeights"]!.AsObject())
        {
            var weights = new Dictionary<Attr, double>();
            foreach (var (attrKey, weightNode) in node!.AsObject())
                weights[Enum.Parse<Attr>(attrKey)] = weightNode!.GetValue<double>();
            positionWeights[Enum.Parse<Position>(posKey)] = weights;
        }

        return new WorldCalibration(constants, ageMult, bands, homeAdv, stadiumProfile, positionWeights);
    }

    public static Dictionary<string, PrestigeBand> BuildClubBands()
    {
        var map = new Dictionary<string, PrestigeBand>();
        foreach (var clubNode in Data["clubs"]!.AsArray())
        {
            var club = clubNode!.AsObject();
            string clubId = club["clubId"]!.GetValue<string>();
            var band = Enum.Parse<PrestigeBand>(club["world"]!["prestigeBand"]!.GetValue<string>());
            map[clubId] = band;
        }
        return map;
    }

    public static Dictionary<Attr, int> ParseAttrs(JsonObject attrsNode)
    {
        var dict = new Dictionary<Attr, int>();
        foreach (var (key, value) in attrsNode)
            dict[Enum.Parse<Attr>(key)] = value!.GetValue<int>();
        return dict;
    }
}
