using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Infrastructure.Sqlite.Repositories;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// Owns a single open SQLite connection and an optional ambient transaction, and
/// exposes the repositories over it. Disposing closes the connection.
/// </summary>
public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;
    private bool _disposed;

    public SqliteUnitOfWork(SqliteConnection connection)
    {
        _connection = connection;

        Func<SqliteTransaction?> accessor = () => _transaction;
        Players = new PlayerRepository(connection, accessor);
        Teams = new TeamRepository(connection, accessor);
        Leagues = new LeagueRepository(connection, accessor);
        PlayerTraits = new PlayerTraitRepository(connection, accessor);
        Matches = new MatchRepository(connection, accessor);
        Standings = new StandingRepository(connection, accessor);
    }

    public IPlayerRepository Players { get; }
    public ITeamRepository Teams { get; }
    public ILeagueRepository Leagues { get; }
    public IPlayerTraitRepository PlayerTraits { get; }
    public IMatchRepository Matches { get; }
    public IStandingRepository Standings { get; }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _transaction = _connection.BeginTransaction();
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
}
