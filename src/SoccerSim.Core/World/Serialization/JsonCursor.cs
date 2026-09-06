using System.Text.Json.Nodes;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// A position inside the world document that knows its own path, so every failure reports
/// where it happened (<c>clubs[3].kits.home.shirt</c>) rather than "input string was not in a
/// correct format". Strict by design: a missing or wrongly-typed field throws rather than
/// defaulting, because a silently defaulted club is worse than a rejected one.
/// </summary>
internal sealed class JsonCursor
{
    private readonly JsonObject _node;

    public JsonCursor(JsonObject node, string path)
    {
        _node = node;
        Path = path;
    }

    public string Path { get; }

    public static JsonCursor ForObject(JsonNode? node, string path)
    {
        if (node is not JsonObject obj)
            throw new WorldFieldException(path, "expected an object");
        return new JsonCursor(obj, path);
    }

    public JsonCursor Object(string name) => ForObject(Required(name), $"{Path}.{name}");

    public JsonArray Array(string name)
    {
        if (Required(name) is not JsonArray array)
            throw new WorldFieldException($"{Path}.{name}", "expected an array");
        return array;
    }

    /// <summary>
    /// Enumerates a child object whose KEYS are data rather than schema (e.g. stadium profiles
    /// keyed by country id), yielding a cursor per entry so failures still report their path.
    /// </summary>
    public IEnumerable<(string Key, JsonCursor Value)> ObjectEntries(string name)
    {
        JsonCursor child = Object(name);
        foreach ((string key, JsonNode? value) in child._node)
            yield return (key, ForObject(value, $"{child.Path}.{key}"));
    }

    public string String(string name)
    {
        string? value = Value<string>(Required(name), $"{Path}.{name}", "a string");
        if (string.IsNullOrWhiteSpace(value))
            throw new WorldFieldException($"{Path}.{name}", "must not be empty");
        return value;
    }

    public string? OptionalString(string name)
    {
        JsonNode? node = _node[name];
        if (node is null)
            return null;
        string? value = Value<string>(node, $"{Path}.{name}", "a string");
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public int Int(string name) => Value<int>(Required(name), $"{Path}.{name}", "an integer");

    public double Double(string name) => Value<double>(Required(name), $"{Path}.{name}", "a number");

    public double? OptionalDouble(string name)
    {
        JsonNode? node = _node[name];
        return node is null ? null : Value<double>(node, $"{Path}.{name}", "a number");
    }

    /// <summary>Reads a 0/1 integer flag (how the source encodes booleans) or a real JSON bool.</summary>
    public bool Flag(string name)
    {
        JsonNode node = Required(name);
        string path = $"{Path}.{name}";

        if (node.AsValue().TryGetValue(out bool boolean))
            return boolean;

        int flag = Value<int>(node, path, "0 or 1");
        return flag switch
        {
            0 => false,
            1 => true,
            _ => throw new WorldFieldException(path, $"expected 0 or 1, found {flag}"),
        };
    }

    /// <summary>
    /// Parses a closed enum, exact-match only. <paramref name="normalize"/> maps the source
    /// spelling onto the schema's (e.g. "Rotação" → "Rotacao") — anything it does not recognise
    /// is rejected rather than guessed at, so a new value in the data is a loud failure and not
    /// a silent default.
    /// </summary>
    public TEnum Enum<TEnum>(string name, Func<string, string>? normalize = null)
        where TEnum : struct, Enum
    {
        string raw = String(name);
        string normalized = normalize is null ? raw : normalize(raw);

        if (!System.Enum.TryParse(normalized, ignoreCase: false, out TEnum parsed) || !System.Enum.IsDefined(parsed))
        {
            string allowed = string.Join(", ", System.Enum.GetNames<TEnum>());
            throw new WorldFieldException($"{Path}.{name}",
                $"'{raw}' is not a valid {typeof(TEnum).Name} (allowed: {allowed})");
        }

        return parsed;
    }

    private JsonNode Required(string name) =>
        _node[name] ?? throw new WorldFieldException($"{Path}.{name}", "missing required field");

    private static T Value<T>(JsonNode node, string path, string expected)
    {
        if (node is not JsonValue value || !value.TryGetValue(out T? parsed) || parsed is null)
            throw new WorldFieldException(path, $"expected {expected}");
        return parsed;
    }
}
