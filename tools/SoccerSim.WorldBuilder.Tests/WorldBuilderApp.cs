using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SoccerSim.Core.World.Import;
using SoccerSim.Infrastructure.Sqlite;

namespace SoccerSim.WorldBuilder.Tests;

/// <summary>
/// The tool hosted against a throwaway database with the real batch imported — the same path a
/// user takes (<c>worldbuilder import</c>, then open the tool), so the tests exercise the data
/// the tool actually ships against rather than a fixture invented for them.
///
/// <para>
/// A file database rather than the in-memory one the Core tests use: the app opens a connection
/// per request, and a shared in-memory database only lives while some connection is open.
/// </para>
/// </summary>
public sealed class WorldBuilderApp : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"worldbuilder-test-{Guid.NewGuid():N}.db");

    /// <summary>The tool with the real batch loaded. Parameterless because xUnit's
    /// <c>IClassFixture</c> requires it — a defaulted parameter does not count.</summary>
    public WorldBuilderApp()
        : this(importWorld: true)
    {
    }

    /// <summary>The tool against a migrated but empty database: the first-run state.</summary>
    public static WorldBuilderApp Empty() => new(importWorld: false);

    private WorldBuilderApp(bool importWorld)
    {
        var factory = SqliteConnectionFactory.ForFile(_databasePath);
        new MigrationRunner(factory).Migrate();

        if (!importWorld)
            return;

        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "world.json"));
        var unitOfWork = new SqliteWorldUnitOfWork(factory.Open());
        try
        {
            new WorldImporter(unitOfWork).ImportAsync(json).GetAwaiter().GetResult();
        }
        finally
        {
            unitOfWork.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["World:DatabasePath"] = _databasePath,
            }));

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test run over.
        }
    }
}
