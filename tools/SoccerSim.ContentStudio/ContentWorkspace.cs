using System.Text.Json;
using SoccerSim.Content;
using SoccerSim.Content.Model;
using SoccerSim.Content.Serialization;
using SoccerSim.Content.Validation;

namespace SoccerSim.ContentStudio;

/// <summary>
/// The editing session: one bundle held in memory, written straight back to the JSON directory
/// after every mutation.
///
/// There is deliberately no separate authoring database. The JSON in the repository IS the
/// working copy, so "save" and "export" are the same act, a crash loses nothing, and a
/// <c>git pull</c> is picked up by restarting the tool. Write-through costs one file write per
/// request, which at content scale is nothing next to the complexity of keeping a second store
/// in sync.
/// </summary>
public sealed class ContentWorkspace
{
    private readonly object _gate = new();
    private readonly string _contentDirectory;
    private ContentBundle _bundle;

    public ContentWorkspace(string contentDirectory)
    {
        _contentDirectory = Resolve(contentDirectory);

        // Starting from an empty bundle when the directory is merely mistyped would be far
        // worse than refusing: the first edit writes that empty bundle straight back to disk.
        // A new bundle has to be created deliberately, not by accident.
        if (!File.Exists(Path.Combine(_contentDirectory, ContentBundleFiles.ManifestFileName)))
        {
            throw new ContentFormatException(
                $"No content bundle at '{_contentDirectory}'. Pass --content <dir> pointing at one, "
                + "or create it with: dotnet run --project tools/SoccerSim.ContentCli -- import-legacy");
        }

        _bundle = ContentBundleFiles.Read(_contentDirectory);
    }

    /// <summary>
    /// Resolves a relative content path against the repository root rather than the current
    /// directory, because <c>dotnet run --project</c> runs with the project folder as its
    /// working directory — so the obvious <c>--content content/dev</c> would otherwise miss.
    /// </summary>
    private static string Resolve(string contentDirectory)
    {
        if (Path.IsPathRooted(contentDirectory))
            return Path.GetFullPath(contentDirectory);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SoccerDreamGame.sln")))
            directory = directory.Parent;

        return directory is null
            ? Path.GetFullPath(contentDirectory)
            : Path.GetFullPath(Path.Combine(directory.FullName, contentDirectory));
    }

    public string ContentDirectory => _contentDirectory;

    public ContentBundle Bundle
    {
        get { lock (_gate) { return _bundle; } }
    }

    public IReadOnlyList<IContentEntity> List(string category)
    {
        Handler handler = HandlerFor(category);
        lock (_gate)
        {
            return handler.Read(_bundle);
        }
    }

    public IContentEntity? Find(string category, string key)
        => List(category).FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Creates or replaces an entity. A payload without an id gets the next free one, so the
    /// user never has to think about numbers — but an explicit id is honoured, which is what
    /// makes a re-import or a hand-written file round-trip unchanged.
    /// </summary>
    public IContentEntity Upsert(string category, JsonElement payload)
    {
        Handler handler = HandlerFor(category);

        lock (_gate)
        {
            IReadOnlyList<IContentEntity> current = handler.Read(_bundle);
            IContentEntity parsed = handler.Parse(payload);

            if (parsed.Id <= 0)
            {
                int nextId = current.Count == 0 ? 1 : current.Max(e => e.Id) + 1;
                parsed = handler.WithId(parsed, nextId);
            }

            var updated = current.Where(e => !string.Equals(e.Key, parsed.Key, StringComparison.Ordinal)).ToList();
            updated.Add(parsed);

            _bundle = handler.Write(_bundle, updated);
            Save();
            return parsed;
        }
    }

    public bool Delete(string category, string key)
    {
        Handler handler = HandlerFor(category);

        lock (_gate)
        {
            IReadOnlyList<IContentEntity> current = handler.Read(_bundle);
            var remaining = current.Where(e => !string.Equals(e.Key, key, StringComparison.Ordinal)).ToList();
            if (remaining.Count == current.Count)
                return false;

            _bundle = handler.Write(_bundle, remaining);
            Save();
            return true;
        }
    }

    /// <summary>Creates or replaces many entities in one transaction-like batch.</summary>
    public int UpsertMany(string category, IEnumerable<JsonElement> payloads)
    {
        Handler handler = HandlerFor(category);

        lock (_gate)
        {
            var byKey = handler.Read(_bundle).ToDictionary(e => e.Key, StringComparer.Ordinal);
            int nextId = byKey.Count == 0 ? 1 : byKey.Values.Max(e => e.Id) + 1;
            int count = 0;

            foreach (JsonElement payload in payloads)
            {
                IContentEntity parsed = handler.Parse(payload);
                if (parsed.Id <= 0)
                {
                    // Reuse the existing id when overwriting, so a re-import of the same rows
                    // does not renumber content a save might already point at.
                    parsed = handler.WithId(
                        parsed,
                        byKey.TryGetValue(parsed.Key, out IContentEntity? existing) ? existing.Id : nextId++);
                }

                byKey[parsed.Key] = parsed;
                count++;
            }

            _bundle = handler.Write(_bundle, byKey.Values.ToList());
            Save();
            return count;
        }
    }

    public ContentValidationResult Validate() => ContentValidator.Default.Validate(Bundle);

    /// <summary>Key lists per category, for the UI's reference dropdowns.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> References()
    {
        ContentBundle bundle = Bundle;
        return ContentCategory.Editable.ToDictionary(
            category => category,
            category => (IReadOnlyList<string>)Handlers[category].Read(bundle)
                .Select(e => e.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>Row counts per category, including the read-only world sections.</summary>
    public IReadOnlyDictionary<string, int> Counts() => Bundle.CountByCategory();

    /// <summary>Validates, then writes the playable content.db. Refuses on any error.</summary>
    public string Build(string outputPath)
    {
        ContentBundle bundle = Bundle;
        ContentValidator.Default.Validate(bundle).ThrowIfInvalid();

        // Same repo-root resolution as the content directory, so the artifact lands in the
        // repository's build/ rather than inside this tool's project folder.
        string resolved = Resolve(outputPath);
        Infrastructure.Sqlite.Content.ContentDbBuilder.Build(bundle, resolved);
        return resolved;
    }

    private void Save()
    {
        ContentManifest manifest = ContentBundleFiles.Write(_bundle, _contentDirectory, "SoccerSim.ContentStudio");
        _bundle = _bundle with { Manifest = manifest };
    }

    private static Handler HandlerFor(string category) =>
        Handlers.TryGetValue(category, out Handler? handler)
            ? handler
            : throw new KeyNotFoundException($"Unknown content category '{category}'.");

    public static bool IsKnownCategory(string category) => Handlers.ContainsKey(category);

    /// <summary>
    /// Per-category plumbing: read the list off a bundle, write a new list back, parse a JSON
    /// payload, and stamp an id. Explicit rather than reflective so a typo is a compile error.
    /// </summary>
    private sealed record Handler(
        Func<ContentBundle, IReadOnlyList<IContentEntity>> Read,
        Func<ContentBundle, IReadOnlyList<IContentEntity>, ContentBundle> Write,
        Func<JsonElement, IContentEntity> Parse,
        Func<IContentEntity, int, IContentEntity> WithId);

    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.Ordinal)
    {
        [ContentCategory.Nations] = new(
            b => b.Nations,
            (b, list) => b with { Nations = Cast<ContentNation>(list) },
            Parse<ContentNation>,
            (e, id) => ((ContentNation)e) with { Id = id }),

        [ContentCategory.Stadiums] = new(
            b => b.Stadiums,
            (b, list) => b with { Stadiums = Cast<ContentStadium>(list) },
            Parse<ContentStadium>,
            (e, id) => ((ContentStadium)e) with { Id = id }),

        [ContentCategory.Competitions] = new(
            b => b.Competitions,
            (b, list) => b with { Competitions = Cast<ContentCompetition>(list) },
            Parse<ContentCompetition>,
            (e, id) => ((ContentCompetition)e) with { Id = id }),

        [ContentCategory.Traits] = new(
            b => b.Traits,
            (b, list) => b with { Traits = Cast<ContentTrait>(list) },
            Parse<ContentTrait>,
            (e, id) => ((ContentTrait)e) with { Id = id }),

        [ContentCategory.Leagues] = new(
            b => b.Leagues,
            (b, list) => b with { Leagues = Cast<ContentLeague>(list) },
            Parse<ContentLeague>,
            (e, id) => ((ContentLeague)e) with { Id = id }),

        [ContentCategory.Teams] = new(
            b => b.Teams,
            (b, list) => b with { Teams = Cast<ContentTeam>(list) },
            Parse<ContentTeam>,
            (e, id) => ((ContentTeam)e) with { Id = id }),

        [ContentCategory.Players] = new(
            b => b.Players,
            (b, list) => b with { Players = Cast<ContentPlayer>(list) },
            Parse<ContentPlayer>,
            (e, id) => ((ContentPlayer)e) with { Id = id }),

        [ContentCategory.Coaches] = new(
            b => b.Coaches,
            (b, list) => b with { Coaches = Cast<ContentCoach>(list) },
            Parse<ContentCoach>,
            (e, id) => ((ContentCoach)e) with { Id = id }),

        [ContentCategory.Contracts] = new(
            b => b.Contracts,
            (b, list) => b with { Contracts = Cast<ContentContract>(list) },
            Parse<ContentContract>,
            (e, id) => ((ContentContract)e) with { Id = id }),

        [ContentCategory.HousingItems] = new(
            b => b.HousingItems,
            (b, list) => b with { HousingItems = Cast<ContentHousingItem>(list) },
            Parse<ContentHousingItem>,
            (e, id) => ((ContentHousingItem)e) with { Id = id }),
    };

    private static IReadOnlyList<T> Cast<T>(IReadOnlyList<IContentEntity> list) where T : IContentEntity
        => list.Cast<T>().OrderBy(e => e.Key, StringComparer.Ordinal).ToList();

    private static IContentEntity Parse<T>(JsonElement payload) where T : IContentEntity
    {
        try
        {
            return payload.Deserialize<T>(ContentJson.Options)
                   ?? throw new ContentFormatException("Request body was null.");
        }
        catch (JsonException ex)
        {
            throw new ContentFormatException($"Could not read the submitted entity: {ex.Message}", ex);
        }
    }
}
