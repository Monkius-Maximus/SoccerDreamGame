using Microsoft.Data.Sqlite;
using SoccerSim.Core.World.Import;
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

    public static async Task<int> RunAsync(string[] args)
    {
        return args[0] switch
        {
            "import" => await ImportAsync(args),
            "--help" or "-h" or "help" => Usage(exitCode: 0),
            _ => Usage(exitCode: 1, $"Unknown command '{args[0]}'."),
        };
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

            Importing is all-or-nothing: a document with any malformed record is rejected in full,
            with one message per record, and nothing is written.
            """);

        return exitCode;
    }
}
