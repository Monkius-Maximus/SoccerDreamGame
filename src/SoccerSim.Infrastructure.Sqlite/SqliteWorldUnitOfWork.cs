using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Infrastructure.Sqlite.Repositories;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// Transaction boundary for the world-authoring schema, over a single open connection — the same
/// shape as <see cref="SqliteUnitOfWork"/>, kept separate because the two schemas are separate
/// (docs/adr/0002-clubidentity-v2-coexistence.md). Both can point at the same database file.
/// </summary>
public sealed class SqliteWorldUnitOfWork : IWorldUnitOfWork
{
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;
    private WorldCalibration? _cachedCalibration;
    private bool _disposed;

    public SqliteWorldUnitOfWork(SqliteConnection connection)
    {
        _connection = connection;

        Func<SqliteTransaction?> transaction = () => _transaction;
        var calibration = new CalibrationRepository(connection, transaction, InvalidateCalibration);

        GeoNodes = new GeoNodeRepository(connection, transaction);
        Clubs = new ClubRepository(connection, transaction, RequireCalibration);
        Characters = new CharacterRepository(connection, transaction, RequireCalibration);
        Competitions = new CompetitionRepository(connection, transaction);
        Calibration = calibration;
        Sources = new WorldSourceRepository(connection, transaction);
        Edits = new WorldEditLog(connection, transaction);
        GenerationProfiles = new GenerationProfileRepository(connection, transaction);
        Settings = new WorldSettingsRepository(connection, transaction);
        Countries = new CountryRepository(connection, transaction);
        Divisions = new DivisionRepository(connection, transaction);
    }

    public IGeoNodeRepository GeoNodes { get; }
    public IClubRepository Clubs { get; }
    public ICharacterRepository Characters { get; }
    public ICompetitionRepository Competitions { get; }
    public ICalibrationRepository Calibration { get; }
    public IWorldSourceRepository Sources { get; }
    public IWorldEditLog Edits { get; }
    public IGenerationProfileRepository GenerationProfiles { get; }
    public IWorldSettingsRepository Settings { get; }
    public ICountryRepository Countries { get; }
    public IDivisionRepository Divisions { get; }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _transaction = _connection.BeginTransaction();

        // Derby rivalries are mutual (A's rival is B, B's rival is A), so no insertion order can
        // satisfy Clubs.DerbyRivalClubId row by row. Deferring foreign keys to COMMIT lets a
        // consistent set of clubs go in together, while still rejecting a set that is genuinely
        // dangling. SQLite resets this at the end of the transaction.
        using SqliteCommand pragma = _connection.CreateCommand();
        pragma.Transaction = _transaction;
        pragma.CommandText = "PRAGMA defer_foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _transaction?.Commit();
        _transaction?.Dispose();
        _transaction = null;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _transaction?.Rollback();
        _transaction?.Dispose();
        _transaction = null;

        // Anything read during the rolled-back transaction is gone from the database, so the
        // cached copy must not outlive it.
        InvalidateCalibration();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Calibration backs every derived-field recalculation, so it would otherwise be re-read
    /// once per club and per character (nearly 700 times during an import). It is cached for the
    /// lifetime of the unit of work and dropped whenever it is rewritten or a transaction rolls
    /// back, so a write never recalculates against numbers that are no longer in the database.
    /// </summary>
    private WorldCalibration RequireCalibration() =>
        _cachedCalibration ??= Calibration.GetAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException(
                "No calibration is loaded. Import a world document (worldbuilder import <file>) before " +
                "writing clubs or characters — their derived fields cannot be computed without it.");

    private void InvalidateCalibration() => _cachedCalibration = null;
}
