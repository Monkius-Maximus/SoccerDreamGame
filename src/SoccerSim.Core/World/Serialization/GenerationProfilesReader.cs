using System.Text.Json.Nodes;
using SoccerSim.Core.World.Generation;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// Reads <c>gen_profiles.json</c>: the per-position attribute shapes measured from the 688 real
/// players, and the name pools drawn from them. Same rules as the world reader — strict, and
/// all-or-nothing.
/// </summary>
public static class GenerationProfilesReader
{
    public static GenerationProfiles Read(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new WorldImportException(["document root: expected a JSON object"]);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new WorldImportException([$"document root: not valid JSON ({ex.Message})"]);
        }

        var errors = new List<string>();
        var cursor = new JsonCursor(root, "$");

        var attributes = new Dictionary<Position, IReadOnlyDictionary<Attr, AttributeProfile>>();
        try
        {
            JsonCursor profiles = cursor.Object("profiles");
            foreach (Position position in Enum.GetValues<Position>())
            {
                JsonCursor row = profiles.Object(position.ToString());
                var shapes = new Dictionary<Attr, AttributeProfile>();

                foreach (Attr attr in Enum.GetValues<Attr>())
                {
                    // Each attribute is an [offsetMean, standardDeviation] pair.
                    JsonArray pair = row.Array(attr.ToString());
                    if (pair.Count != 2)
                    {
                        throw new WorldFieldException($"profiles.{position}.{attr}",
                            "expected an [offsetMean, standardDeviation] pair");
                    }

                    shapes[attr] = new AttributeProfile(
                        pair[0]!.GetValue<double>(),
                        pair[1]!.GetValue<double>());
                }

                attributes[position] = shapes;
            }
        }
        catch (WorldFieldException ex)
        {
            errors.Add(ex.Message);
        }

        IReadOnlyList<string> firstNames = ReadNames(errors, cursor, "firstNames");
        IReadOnlyList<string> lastNames = ReadNames(errors, cursor, "lastNames");

        if (errors.Count > 0)
            throw new WorldImportException(errors);

        return new GenerationProfiles(attributes, firstNames, lastNames);
    }

    private static IReadOnlyList<string> ReadNames(List<string> errors, JsonCursor cursor, string section)
    {
        try
        {
            JsonArray array = cursor.Array(section);
            var names = new List<string>(array.Count);

            for (int i = 0; i < array.Count; i++)
            {
                string? name = array[i]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                    throw new WorldFieldException($"{section}[{i}]", "expected a name");
                names.Add(name);
            }

            if (names.Count == 0)
                throw new WorldFieldException(section, "must not be empty");

            return names;
        }
        catch (WorldFieldException ex)
        {
            errors.Add(ex.Message);
            return [];
        }
    }
}
