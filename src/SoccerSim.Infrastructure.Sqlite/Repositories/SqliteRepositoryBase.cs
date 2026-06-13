using Microsoft.Data.Sqlite;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Shared plumbing for the SQLite repositories. Each repository creates commands on
/// the unit-of-work's single connection and enlists them in the current transaction.
/// SQLite is synchronous under the hood, so the async repository methods complete
/// synchronously and return completed tasks — honest about what the engine does.
/// </summary>
internal abstract class SqliteRepositoryBase
{
    protected SqliteRepositoryBase(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
    {
        Connection = connection;
        TransactionAccessor = transactionAccessor;
    }

    protected SqliteConnection Connection { get; }

    protected Func<SqliteTransaction?> TransactionAccessor { get; }

    protected SqliteCommand CreateCommand(string sql)
    {
        SqliteCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = TransactionAccessor();
        return command;
    }
}
