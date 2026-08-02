using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoccerSim.Content.Serialization;

/// <summary>
/// The canonical JSON settings for content. "Canonical" matters: the manifest's content hash is
/// computed over this exact output, so two builds of identical content must serialize
/// byte-for-byte identically.
/// </summary>
public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            // Dates are written round-trippable ("2026-08-01T00:00:00"), matching the
            // ISO-8601 TEXT convention the SQL schema already uses.
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json, string sourceName)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                   ?? throw new ContentFormatException($"{sourceName} deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentFormatException($"{sourceName} is not valid content JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// SHA-256 over the given payloads, in the order supplied. Returned as
    /// <c>sha256:&lt;lowercase hex&gt;</c> so the algorithm is visible in the manifest and can be
    /// changed later without ambiguity.
    /// </summary>
    public static string Hash(IEnumerable<string> payloads)
    {
        using var sha = SHA256.Create();
        using var stream = new MemoryStream();
        foreach (string payload in payloads)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            stream.Write(bytes, 0, bytes.Length);
            stream.WriteByte((byte)'\n');
        }

        stream.Position = 0;
        return "sha256:" + Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
