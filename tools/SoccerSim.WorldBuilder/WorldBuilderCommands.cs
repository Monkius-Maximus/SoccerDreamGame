using System.Globalization;
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

    private static readonly string[] Verbs = ["import", "import-profiles", "import-club-profiles", "generate-club", "generate-division", "project", "help", "--help", "-h"];

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
            "import-club-profiles" => await ImportClubProfilesAsync(args),
            "generate-club" => await GenerateClubAsync(args),
            "generate-division" => await GenerateDivisionAsync(args),
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

    /// <summary>
    /// worldbuilder import-club-profiles &lt;club_profiles.json&gt; [database]
    ///
    /// <para>One country per document (ADR-0011 §3). Importing a country again replaces that
    /// country's profiles and leaves the others alone.</para>
    /// </summary>
    private static async Task<int> ImportClubProfilesAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage(exitCode: 1,
                "import-club-profiles needs a profile document: worldbuilder import-club-profiles <file.json> [database]");
        }

        string documentPath = args[1];
        string databasePath = args.Length > 2 ? args[2] : DefaultDatabase;

        if (!File.Exists(documentPath))
        {
            Console.Error.WriteLine($"Club profile document not found: {documentPath}");
            return 1;
        }

        string json = await File.ReadAllTextAsync(documentPath);

        var factory = SqliteConnectionFactory.ForFile(databasePath);
        new MigrationRunner(factory).Migrate();

        await using var unitOfWork = new SqliteWorldUnitOfWork(factory.Open());

        try
        {
            // One statement, so no transaction: the document is validated before it is written.
            ClubProfiles profiles = await unitOfWork.ClubProfiles.SaveAsync(json);

            Console.WriteLine(
                $"Imported club profiles for {profiles.CountryId} ({profiles.Cities.Count} cities) into {databasePath}.");
            return 0;
        }
        catch (WorldImportException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// worldbuilder generate-club &lt;countryId&gt; &lt;band&gt; &lt;strength&gt; &lt;seed&gt; [database]
    ///
    /// <para>Writes one Regen club through <see cref="ClubCreation"/>, the same path the web tool's
    /// "Gerar clube" takes, so it lands on the undo stack there too. Strength uses a dot decimal:
    /// it is a command-line number, not the CSV dialect.</para>
    /// </summary>
    private static async Task<int> GenerateClubAsync(string[] args)
    {
        const string usage = "generate-club needs: worldbuilder generate-club <countryId> <band> <strength> <seed> [database]";
        if (args.Length < 5)
            return Usage(exitCode: 1, usage);

        if (!Enum.TryParse(args[2], ignoreCase: false, out PrestigeBand band) || !Enum.IsDefined(band))
        {
            return Usage(exitCode: 1,
                $"'{args[2]}' is not a prestige band (allowed: {string.Join(", ", Enum.GetNames<PrestigeBand>())}).");
        }

        if (!double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double strength))
            return Usage(exitCode: 1, $"'{args[3]}' is not a strength; use a dot decimal in (0, 1], e.g. 0.55.");

        if (!long.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed))
            return Usage(exitCode: 1, $"'{args[4]}' is not a seed; use an integer.");

        string databasePath = args.Length > 5 ? args[5] : DefaultDatabase;

        var factory = SqliteConnectionFactory.ForFile(databasePath);
        new MigrationRunner(factory).Migrate();

        await using var unitOfWork = new SqliteWorldUnitOfWork(factory.Open());

        try
        {
            ClubIdentity club = await ClubCreation.ApplyAsync(
                unitOfWork, new ClubGenerationRequest(args[1], band, strength, seed));

            Console.WriteLine(
                $"Generated {club.ClubId} {club.Identity.OfficialName} ({club.Geography.CityName}/{club.Geography.Uf}) into {databasePath}.");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// worldbuilder generate-division &lt;countryId&gt; &lt;divisionId&gt; &lt;clubCount&gt; &lt;band&gt;
    /// &lt;strengthMin&gt; &lt;strengthMax&gt; &lt;seed&gt; [database]
    ///
    /// <para>Fills a division with Regen clubs and their squads through
    /// <see cref="DivisionCreation"/>, the same path the web tool's "Gerar divisão" takes: one
    /// transaction, one step on the undo stack. Strengths use dot decimals, like
    /// <c>generate-club</c>.</para>
    /// </summary>
    private static async Task<int> GenerateDivisionAsync(string[] args)
    {
        const string usage = "generate-division needs: worldbuilder generate-division <countryId> <divisionId> "
            + "<clubCount> <band> <strengthMin> <strengthMax> <seed> [database]";
        if (args.Length < 8)
            return Usage(exitCode: 1, usage);

        if (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clubCount))
            return Usage(exitCode: 1, $"'{args[3]}' is not a club count; use an integer.");

        if (!Enum.TryParse(args[4], ignoreCase: false, out PrestigeBand band) || !Enum.IsDefined(band))
        {
            return Usage(exitCode: 1,
                $"'{args[4]}' is not a prestige band (allowed: {string.Join(", ", Enum.GetNames<PrestigeBand>())}).");
        }

        if (!double.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double strengthMin))
            return Usage(exitCode: 1, $"'{args[5]}' is not a strength; use a dot decimal in (0, 1], e.g. 0.55.");

        if (!double.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double strengthMax))
            return Usage(exitCode: 1, $"'{args[6]}' is not a strength; use a dot decimal in (0, 1], e.g. 0.80.");

        if (!long.TryParse(args[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed))
            return Usage(exitCode: 1, $"'{args[7]}' is not a seed; use an integer.");

        string databasePath = args.Length > 8 ? args[8] : DefaultDatabase;

        var factory = SqliteConnectionFactory.ForFile(databasePath);
        new MigrationRunner(factory).Migrate();

        await using var unitOfWork = new SqliteWorldUnitOfWork(factory.Open());

        try
        {
            DivisionGenerationResult batch = await DivisionCreation.ApplyAsync(
                unitOfWork,
                new DivisionGenerationRequest(args[1], args[2], clubCount, band, strengthMin, strengthMax, seed));

            foreach (ClubIdentity club in batch.Clubs)
            {
                Console.WriteLine(
                    $"  {club.ClubId} {club.Identity.OfficialName} ({club.Geography.CityName}/{club.Geography.Uf}) "
                    + $"strength {club.World.ClubStrength.ToString("0.00", CultureInfo.InvariantCulture)}");
            }

            Console.WriteLine(
                $"Generated {batch.Clubs.Count} clubs and {batch.Characters.Count} players into {args[2]} in {databasePath}.");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
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
              worldbuilder import-club-profiles <f> [db]
                                                        load one country's club-generation profiles
              worldbuilder generate-club <countryId> <band> <strength> <seed> [db]
                                                        generate one Regen club (strength in (0, 1])
              worldbuilder generate-division <countryId> <divisionId> <clubCount> <band> <strengthMin> <strengthMax> <seed> [db]
                                                        fill a division with Regen clubs and squads
              worldbuilder project [db]                 rewrite the legacy game tables from the world

            Importing is all-or-nothing: a document with any malformed record is rejected in full,
            with one message per record, and nothing is written. Projecting is the same: it either
            writes the whole playable world or leaves the tables untouched.
            """);

        return exitCode;
    }
}
