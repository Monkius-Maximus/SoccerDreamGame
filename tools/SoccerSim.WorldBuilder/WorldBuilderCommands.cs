using Microsoft.Data.Sqlite;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;
using SoccerSim.Core.World.Import;
using SoccerSim.Core.World.Projection;
using SoccerSim.Core.World.Serialization;
using SoccerSim.Infrastructure.Sqlite;

namespace SoccerSim.WorldBuilder;

/// <summary>
/// The tool's command-line operations. Thin by design: argument parsing and reporting live here,
/// everything the import actually does lives in <see cref="WorldImporter"/> in Core, so the same
/// logic runs from the HTTP surface later without being reimplemented.
/// </summary>
internal static class WorldBuilderCommands
{
    private const string DefaultDatabase = "world.db";

    private static readonly string[] Verbs = ["import", "import-profiles", "project", "help", "--help", "-h"];

    /// <summary>
    /// Whether these arguments are one of the tool's own commands. Everything else — including
    /// the host arguments a test or a launch profile passes — belongs to the web host, so the
    /// command line does not have to know about every ASP.NET Core switch to avoid eating it.
    /// </summary>
    public static bool IsCommand(string[] args) => args.Length > 0 && Verbs.Contains(args[0]);

    public static async Task<int> RunAsync(string[] args)
    {
        return args[0] switch
        {
            "import" => await ImportAsync(args),
            "import-profiles" => await ImportProfilesAsync(args),
            "project" => await ProjectAsync(args),
            _ => Usage(exitCode: 0),
        };
    }

    /// <summary>
    /// worldbuilder project [database]
    ///
    /// <para>Rewrites the legacy <c>Leagues</c>/<c>Seasons</c>/<c>Teams</c>/<c>Players</c> tables
    /// from the authored world, so a club created in the tool becomes playable by the
    /// <c>MatchEngine</c> that already exists (ROADMAP.md Sprint 6). The world itself is only
    /// read — projecting never changes what was authored.</para>
    /// </summary>
    private static async Task<int> ProjectAsync(string[] args)
    {
        string databasePath = args.Length > 1 ? args[1] : DefaultDatabase;

        var factory = SqliteConnectionFactory.ForFile(databasePath);
        new MigrationRunner(factory).Migrate();

        await using var unitOfWork = new SqliteWorldUnitOfWork(factory.Open());

        IReadOnlyList<ClubIdentity> clubs = await unitOfWork.Clubs.ListAsync();
        IReadOnlyList<CharacterRecord> characters = await unitOfWork.Characters.ListAsync();
        IReadOnlyList<Competition> competitions = await unitOfWork.Competitions.ListAsync();
        IReadOnlyList<GeoNode> geoNodes = await unitOfWork.GeoNodes.ListAsync();

        if (clubs.Count == 0)
        {
            Console.Error.WriteLine(
                $"No world is loaded in {databasePath}. Run `worldbuilder import <file.json>` first.");
            return 1;
        }

        try
        {
            LegacyWorld projected = WorldToLegacyProjection.Project(clubs, characters, competitions, geoNodes);

            using SqliteConnection connection = factory.Open();
            new LegacyProjectionWriter(connection).Write(projected);

            Console.WriteLine($"Projected {projected} into {databasePath}.");
            return 0;
        }
        catch (ProjectionException ex)
        {
            // Either the world cannot be represented, or the database has been played. Both are
            // states with a clear remedy, not crashes.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// worldbuilder import-profiles &lt;gen_profiles.json&gt; [database]
    ///
    /// <para>A separate command from <c>import</c> because it is a separate measurement: the
    /// attribute shapes and name pools can be re-measured from a larger batch without touching
    /// the world, and re-importing a world should not silently discard them.</para>
    /// </summary>
    private static async Task<int> ImportProfilesAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage(exitCode: 1,
                "import-profiles needs a profile document: worldbuilder import-profiles <file.json> [database]");
        }

        string documentPath = args[1];
        string databasePath = args.Length > 2 ? args[2] : DefaultDatabase;

        if (!File.Exists(documentPath))
        {
            Console.Error.WriteLine($"Profile document not found: {documentPath}");
            return 1;
        }

        string json = await File.ReadAllTextAsync(documentPath);

        var factory = SqliteConnectionFactory.ForFile(databasePath);
        new MigrationRunner(factory).Migrate();

        await using var unitOfWork = new SqliteWorldUnitOfWork(factory.Open());

        try
        {
            GenerationProfiles profiles = GenerationProfilesReader.Read(json);

            await unitOfWork.BeginTransactionAsync();
            await unitOfWork.GenerationProfiles.SaveAsync(profiles);
            await unitOfWork.CommitAsync();

            Console.WriteLine(
                $"Imported generation profiles for {profiles.Attributes.Count} positions, "
                + $"{profiles.FirstNames.Count} first names and {profiles.LastNames.Count} surnames into {databasePath}.");
            return 0;
        }
        catch (WorldImportException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>worldbuilder import &lt;file.json&gt; [database]</summary>
    private static async Task<int> ImportAsync(string[] args)
    {
        if (args.Length < 2)
            return Usage(exitCode: 1, "import needs a world document: worldbuilder import <file.json> [database]");

        string documentPath = args[1];
        string databasePath = args.Length > 2 ? args[2] : DefaultDatabase;

        if (!File.Exists(documentPath))
        {
            Console.Error.WriteLine($"World document not found: {documentPath}");
            return 1;
        }

        string json = await File.ReadAllTextAsync(documentPath);

        var factory = SqliteConnectionFactory.ForFile(databasePath);
        new MigrationRunner(factory).Migrate();

        await using var unitOfWork = new SqliteWorldUnitOfWork(factory.Open());
        var importer = new WorldImporter(unitOfWork);

        try
        {
            WorldImportReport report = await importer.ImportAsync(json);
            Console.WriteLine($"Imported {report} from {documentPath} into {databasePath}.");
            return 0;
        }
        catch (WorldImportException ex)
        {
            // The document was rejected as a whole; nothing was written. Every malformed record is
            // listed so the data can be fixed in one pass.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Usage(int exitCode, string? error = null)
    {
        if (error is not null)
            Console.Error.WriteLine(error);

        Console.WriteLine(
            """
            SoccerSim.WorldBuilder — Terra Paralela world authoring tool.

              worldbuilder                              start the web tool
              worldbuilder import <file.json> [db]      load a world document (default db: world.db)
              worldbuilder import-profiles <f> [db]     load the squad-generation profiles
              worldbuilder project [db]                 rewrite the legacy game tables from the world

            Importing is all-or-nothing: a document with any malformed record is rejected in full,
            with one message per record, and nothing is written. Projecting is the same: it either
            writes the whole playable world or leaves the tables untouched.
            """);

        return exitCode;
    }
}
