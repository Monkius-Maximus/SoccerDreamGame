using SoccerSim.Content;
using SoccerSim.Content.Serialization;
using SoccerSim.Content.Validation;
using SoccerSim.Infrastructure.Sqlite.Content;

const string Generator = "SoccerSim.ContentCli 0.1.0";
const string DefaultContentDir = "content/dev";
const string DefaultOutputDb = "build/content/content.db";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

try
{
    return args[0] switch
    {
        "validate" => Validate(Arg(args, "--content", DefaultContentDir)),
        "build" => BuildDb(Arg(args, "--content", DefaultContentDir), Arg(args, "--out", DefaultOutputDb)),
        "stats" => Stats(Arg(args, "--content", DefaultContentDir)),
        "reexport" => Reexport(Arg(args, "--content", DefaultContentDir)),
        _ => Unknown(args[0]),
    };
}
catch (ContentValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (ContentFormatException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Validate(string contentDir)
{
    ContentBundle bundle = ContentBundleFiles.Read(contentDir);
    ContentValidationResult result = ContentValidator.Default.Validate(bundle);

    foreach (ContentIssue issue in result.Issues)
        Console.WriteLine(issue);

    if (!ContentBundleFiles.HashMatches(contentDir))
    {
        // A hand-edited JSON that was never re-exported: the manifest hash no longer describes
        // what is on disk, so any save stamped with it would be lying about its contents.
        Console.Error.WriteLine(
            $"ERROR [HASH_STALE] {contentDir}: content hash does not match the manifest. "
            + "Re-export the bundle so manifest.json matches the JSON files.");
        return 1;
    }

    int errors = result.Errors.Count();
    Console.WriteLine(errors == 0
        ? $"OK: {contentDir} is valid ({result.Warnings.Count()} warning(s))."
        : $"FAILED: {errors} error(s), {result.Warnings.Count()} warning(s).");
    return errors == 0 ? 0 : 1;
}

static int BuildDb(string contentDir, string outputPath)
{
    ContentBundle bundle = ContentBundleFiles.Read(contentDir);
    ContentValidator.Default.Validate(bundle).ThrowIfInvalid();

    ContentImportReport report = ContentDbBuilder.Build(bundle, outputPath);
    Console.WriteLine($"Built {outputPath}: {report.Describe()}");
    return 0;
}

static int Reexport(string contentDir)
{
    // Read, validate, write back. Normalises formatting and key order and recomputes the hash,
    // which is what a bundle needs after being edited by hand rather than through the tool.
    ContentBundle bundle = ContentBundleFiles.Read(contentDir);

    ContentValidationResult result = ContentValidator.Default.Validate(bundle);
    foreach (ContentIssue issue in result.Issues)
        Console.WriteLine(issue);
    result.ThrowIfInvalid();

    ContentManifest manifest = ContentBundleFiles.Write(bundle, contentDir, Generator);
    Console.WriteLine($"Re-exported {contentDir}: build {manifest.BuildId} ({manifest.ContentHash})");
    return 0;
}

static int Stats(string contentDir)
{
    ContentBundle bundle = ContentBundleFiles.Read(contentDir);
    Console.WriteLine($"{contentDir} — build {bundle.Manifest.BuildId} ({bundle.Manifest.ContentHash})");
    foreach ((string category, int count) in bundle.CountByCategory().OrderBy(c => c.Key, StringComparer.Ordinal))
        Console.WriteLine($"  {count,6}  {category}");
    return 0;
}

static int Unknown(string verb)
{
    Console.Error.WriteLine($"Unknown command '{verb}'.");
    PrintUsage();
    return 2;
}

static string Arg(string[] args, string name, string fallback)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

static void PrintUsage() => Console.WriteLine(
    """
    SoccerSim content tool.

      validate       [--content <dir>]              Validate a bundle and check its hash.
      build          [--content <dir>] [--out <db>] Validate, then build a playable content.db.
      stats          [--content <dir>]              Row counts per category.
      reexport       [--content <dir>]              Re-normalise and re-hash a hand-edited bundle.

    Defaults: --content content/dev, --out build/content/content.db
    """);
