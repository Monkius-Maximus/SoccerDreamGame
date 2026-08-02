using Microsoft.Data.Sqlite;
using SoccerSim.Core.Economy;
using SoccerSim.Core.Events;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IEconomyService"/> over <c>PlayerFinances</c> and <c>Transactions</c>
/// (migration 0004).
///
/// <para>
/// Every balance change writes a ledger row in the same transaction as the balance update, so the
/// ledger can never disagree with the balance — that is the property that makes a spending
/// breakdown screen trustworthy later.
/// </para>
///
/// <para>
/// Housing multipliers are read for <see cref="ComputeTaskYield"/> but no daily-task table exists
/// yet, so it returns an empty yield rather than inventing one.
/// </para>
/// </summary>
public sealed class SqliteEconomyService : IEconomyService
{
    /// <summary>Ledger amounts are signed: positive is income, negative is spending.</summary>
    private const int ExpenseSign = -1;

    private readonly SqliteConnection _connection;

    public SqliteEconomyService(SqliteConnection connection) => _connection = connection;

    public long GetBalance(int playerId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT Balance FROM PlayerFinances WHERE PlayerId = $pid;";
        command.Parameters.AddWithValue("$pid", playerId);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? 0L : Convert.ToInt64(value);
    }

    public bool CanAfford(int playerId, long amount) => amount <= 0 || GetBalance(playerId) >= amount;

    public bool TryCharge(int playerId, long amount, string category, DateTime date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (amount <= 0)
            return true;

        using SqliteTransaction transaction = _connection.BeginTransaction();

        if (ReadBalance(transaction, playerId) < amount)
            return false; // rolled back on dispose; nothing was written

        AdjustBalance(transaction, playerId, ExpenseSign * amount);
        WriteLedger(transaction, playerId, ExpenseSign * amount, category, date);
        transaction.Commit();
        return true;
    }

    public void Credit(int playerId, long amount, string category, DateTime date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (amount <= 0)
            return;

        using SqliteTransaction transaction = _connection.BeginTransaction();
        AdjustBalance(transaction, playerId, amount);
        WriteLedger(transaction, playerId, amount, category, date);
        transaction.Commit();
    }

    public void ApplyResolution(int playerId, EventResolutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (ResourceDelta delta in result.ResourceDeltas)
        {
            // Only money moves through the ledger; other resource keys belong to their own systems.
            if (!string.Equals(delta.ResourceKey, "money", StringComparison.Ordinal) || delta.Delta == 0)
                continue;

            using SqliteTransaction transaction = _connection.BeginTransaction();
            AdjustBalance(transaction, playerId, delta.Delta);
            WriteLedger(transaction, playerId, delta.Delta, TransactionCategory.Event, DateTime.UtcNow);
            transaction.Commit();
        }
    }

    public TaskYield ComputeTaskYield(int playerId, string taskKey) =>
        // No daily-task table exists yet. Returning an empty yield is honest; inventing a multiplier
        // here would be a number nothing derived.
        new(Array.Empty<StatDelta>());

    private long ReadBalance(SqliteTransaction transaction, int playerId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Balance FROM PlayerFinances WHERE PlayerId = $pid;";
        command.Parameters.AddWithValue("$pid", playerId);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? 0L : Convert.ToInt64(value);
    }

    /// <summary>Upserts, so a player with no finances row yet still gets a correct balance.</summary>
    private void AdjustBalance(SqliteTransaction transaction, int playerId, long delta)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            @"INSERT INTO PlayerFinances (PlayerId, Balance, BaseSalaryWeekly) VALUES ($pid, $delta, 0)
              ON CONFLICT (PlayerId) DO UPDATE SET Balance = Balance + excluded.Balance;";
        command.Parameters.AddWithValue("$pid", playerId);
        command.Parameters.AddWithValue("$delta", delta);
        command.ExecuteNonQuery();
    }

    private void WriteLedger(SqliteTransaction transaction, int playerId, long amount, string category, DateTime date)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            @"INSERT INTO Transactions (PlayerId, Date, Amount, Category)
              VALUES ($pid, $date, $amount, $category);";
        command.Parameters.AddWithValue("$pid", playerId);
        command.Parameters.AddWithValue("$date", SqliteValue.ToText(date));
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$category", category);
        command.ExecuteNonQuery();
    }
}
