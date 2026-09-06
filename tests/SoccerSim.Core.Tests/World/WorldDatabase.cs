using Microsoft.Data.Sqlite;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Import;
using SoccerSim.Infrastructure.Sqlite;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// A migrated, in-memory world database for the Sprint 2 tests. The shared in-memory database
/// lives only while a connection is open, so the caller holds the keep-alive connection for the
/// duration — the same pattern as the legacy SqlitePersistenceTests.
/// </summary>
internal sealed class WorldDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;

    private WorldDatabase(SqliteConnectionFactory factory, SqliteConnection keepAlive)
    {
        Factory = factory;
        _keepAlive = keepAlive;
    }

    public SqliteConnectionFactory Factory { get; }

    public static WorldDatabase Migrated()
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"world-{Guid.NewGuid():N}");
        SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate();
        return new WorldDatabase(factory, keepAlive);
    }

    /// <summary>A migrated database with the real 20-club/688-player batch already imported.</summary>
    public static async Task<WorldDatabase> WithRealWorldImported()
    {
        WorldDatabase database = Migrated();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await new WorldImporter(unitOfWork).ImportAsync(WorldFixture.Json);
        return database;
    }

    /// <summary>
    /// A migrated database with the real calibration and one geo node in place — the minimum a
    /// club can be written against, since club writes recalculate derived fields from calibration
    /// and reference a geo node.
    /// </summary>
    public static async Task<WorldDatabase> ReadyForClubs()
    {
        WorldDatabase database = Migrated();
        await using SqliteWorldUnitOfWork unitOfWork = database.OpenUnitOfWork();
        await unitOfWork.Calibration.SaveAsync(WorldFixture.BuildCalibration());
        await unitOfWork.GeoNodes.AddAsync(WorldSamples.GeoNode());
        return database;
    }

    public SqliteWorldUnitOfWork OpenUnitOfWork() => new(Factory.Open());

    /// <summary>Runs a scalar query against the keep-alive connection, for tests that assert on
    /// raw rows rather than through the repositories.</summary>
    public T Scalar<T>(string sql)
    {
        using SqliteCommand command = _keepAlive.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }

    public async ValueTask DisposeAsync()
    {
        await _keepAlive.DisposeAsync();
    }
}
