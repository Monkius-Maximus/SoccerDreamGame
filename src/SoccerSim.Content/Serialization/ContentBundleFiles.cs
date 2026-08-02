using SoccerSim.Content.Model;
using SoccerSim.Content.Validation;

namespace SoccerSim.Content.Serialization;

/// <summary>
/// Reads and writes a bundle as a directory of JSON files — one per category, plus a manifest.
///
/// One file per category rather than one big file, because that is what makes the content
/// reviewable: a PR that adds a club touches <c>teams.json</c> and nothing else. Entities are
/// written sorted by key so the diff shows only what actually changed.
/// </summary>
public static class ContentBundleFiles
{
    public const string ManifestFileName = "manifest.json";

    public static string FileNameFor(string category) => category + ".json";

    /// <summary>
    /// Writes the bundle to <paramref name="directory"/>, creating it if needed, and returns the
    /// manifest actually written (with the freshly computed hash and counts).
    /// </summary>
    public static ContentManifest Write(ContentBundle bundle, string directory, string generator)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        // Payloads in the fixed ContentCategory.Files order so the hash is stable.
        List<(string File, string Json)> payloads = Payloads(bundle);

        string hash = ContentJson.Hash(payloads.Select(p => p.File + "\n" + p.Json));

        // Keep the recorded provenance when the content is byte-identical. Stamping a fresh
        // timestamp (or a different tool name) on every write would make manifest.json show up
        // in `git status` after a save that changed nothing, and would make an unchanged bundle
        // look like a new build to a save file that already holds it.
        bool unchanged = string.Equals(bundle.Manifest.ContentHash, hash, StringComparison.Ordinal)
                         && !string.IsNullOrEmpty(bundle.Manifest.BuildId);

        var manifest = new ContentManifest
        {
            FormatVersion = ContentSchema.FormatVersion,
            ContentVersion = ContentSchema.CurrentVersion,
            BuildId = unchanged ? bundle.Manifest.BuildId : DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Generator = unchanged ? bundle.Manifest.Generator : generator,
            ContentHash = hash,
            Counts = bundle.CountByCategory(),
        };

        foreach ((string file, string json) in payloads)
            File.WriteAllText(Path.Combine(directory, file), json + Environment.NewLine);

        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            ContentJson.Serialize(manifest) + Environment.NewLine);

        return manifest;
    }

    /// <summary>
    /// Reads a bundle from <paramref name="directory"/>. Throws <see cref="ContentFormatException"/>
    /// if the manifest is missing or declares a version this build cannot read — never returns a
    /// partially understood bundle.
    /// </summary>
    public static ContentBundle Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        return Read(
            fileName =>
            {
                string path = Path.Combine(directory, fileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            },
            directory);
    }

    /// <summary>
    /// Reads a bundle from any source of named JSON documents — a directory, embedded assembly
    /// resources, a zip. <paramref name="readText"/> returns null for a file that is not there.
    /// <paramref name="sourceName"/> only appears in error messages.
    /// </summary>
    public static ContentBundle Read(Func<string, string?> readText, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(readText);

        string manifestJson = readText(ManifestFileName)
            ?? throw new ContentFormatException(
                $"No {ManifestFileName} in '{sourceName}'. Point at a content bundle, "
                + "or generate one with the content tool.");

        ContentManifest manifest = ContentJson.Deserialize<ContentManifest>(manifestJson, ManifestFileName);
        ContentCompatibilityCheck.Validate(manifest);

        return new ContentBundle
        {
            Manifest = manifest,
            Nations = ReadList<ContentNation>(readText, ContentCategory.Nations),
            Stadiums = ReadList<ContentStadium>(readText, ContentCategory.Stadiums),
            Competitions = ReadList<ContentCompetition>(readText, ContentCategory.Competitions),
            Traits = ReadList<ContentTrait>(readText, ContentCategory.Traits),
            Leagues = ReadList<ContentLeague>(readText, ContentCategory.Leagues),
            Teams = ReadList<ContentTeam>(readText, ContentCategory.Teams),
            Players = ReadList<ContentPlayer>(readText, ContentCategory.Players),
            Coaches = ReadList<ContentCoach>(readText, ContentCategory.Coaches),
            Contracts = ReadList<ContentContract>(readText, ContentCategory.Contracts),
            HousingItems = ReadList<ContentHousingItem>(readText, ContentCategory.HousingItems),
            World = ReadOne<ContentWorld>(readText, ContentCategory.World) ?? new ContentWorld(),
        };
    }

    /// <summary>
    /// The canonical (file, json) pairs for a bundle, in <see cref="ContentCategory.Files"/>
    /// order. Single source for both writing and hashing, so the two can never disagree.
    /// </summary>
    private static List<(string File, string Json)> Payloads(ContentBundle bundle) =>
    [
        (FileNameFor(ContentCategory.Nations), ContentJson.Serialize(Sorted(bundle.Nations))),
        (FileNameFor(ContentCategory.Stadiums), ContentJson.Serialize(Sorted(bundle.Stadiums))),
        (FileNameFor(ContentCategory.Competitions), ContentJson.Serialize(Sorted(bundle.Competitions))),
        (FileNameFor(ContentCategory.Traits), ContentJson.Serialize(Sorted(bundle.Traits))),
        (FileNameFor(ContentCategory.Leagues), ContentJson.Serialize(Sorted(bundle.Leagues))),
        (FileNameFor(ContentCategory.Teams), ContentJson.Serialize(Sorted(bundle.Teams))),
        (FileNameFor(ContentCategory.Players), ContentJson.Serialize(Sorted(bundle.Players))),
        (FileNameFor(ContentCategory.Coaches), ContentJson.Serialize(Sorted(bundle.Coaches))),
        (FileNameFor(ContentCategory.Contracts), ContentJson.Serialize(Sorted(bundle.Contracts))),
        (FileNameFor(ContentCategory.HousingItems), ContentJson.Serialize(Sorted(bundle.HousingItems))),
        (FileNameFor(ContentCategory.World), ContentJson.Serialize(SortWorld(bundle.World))),
    ];

    /// <summary>
    /// Recomputes the hash of what is on disk and compares it to the manifest. Catches a JSON
    /// file hand-edited without re-running the export.
    /// </summary>
    public static bool HashMatches(string directory)
    {
        ContentBundle bundle = Read(directory);
        IEnumerable<string> payloads = ContentCategory.Files.Select(category => OnDiskPayload(directory, category));

        return string.Equals(ContentJson.Hash(payloads), bundle.Manifest.ContentHash, StringComparison.Ordinal);
    }

    private static string OnDiskPayload(string directory, string category)
    {
        string path = Path.Combine(directory, FileNameFor(category));
        string json = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        return FileNameFor(category) + "\n" + json.TrimEnd('\r', '\n');
    }

    private static IReadOnlyList<T> ReadList<T>(Func<string, string?> readText, string category)
        => ReadOne<List<T>>(readText, category) ?? [];

    private static T? ReadOne<T>(Func<string, string?> readText, string category) where T : class
    {
        string fileName = FileNameFor(category);
        string? json = readText(fileName);
        return json is null ? null : ContentJson.Deserialize<T>(json, fileName);
    }

    private static List<T> Sorted<T>(IReadOnlyList<T> entities) where T : IContentEntity
        => entities.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();

    private static ContentWorld SortWorld(ContentWorld world) => world with
    {
        Seasons = world.Seasons.OrderBy(s => s.Key, StringComparer.Ordinal).ToList(),
        Fixtures = world.Fixtures.OrderBy(f => f.Key, StringComparer.Ordinal).ToList(),
        Finances = world.Finances.OrderBy(f => f.PlayerKey, StringComparer.Ordinal).ToList(),
    };
}
