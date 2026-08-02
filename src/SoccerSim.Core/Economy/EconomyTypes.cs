using SoccerSim.Core.Events;

namespace SoccerSim.Core.Economy;

/// <summary>The stat changes yielded by completing a simulated daily task.</summary>
public readonly record struct TaskYield(IReadOnlyList<StatDelta> StatDeltas);

/// <summary>Ledger categories. Kept as constants so the same string is never spelled two ways.</summary>
public static class TransactionCategory
{
    public const string Salary = "salary";
    public const string Bonus = "bonus";
    public const string Sponsorship = "sponsorship";
    public const string Housing = "housing";
    public const string Diet = "diet";
    public const string Recovery = "recovery";
    public const string Leisure = "leisure";

    /// <summary>Spending driven by a life-sim activity (the <c>LifeActivity.Cost</c> path).</summary>
    public const string LifeActivity = "life_activity";

    /// <summary>The resource side of a resolved event.</summary>
    public const string Event = "event";
}

/// <summary>
/// Computes income/expenditure and the housing-multiplied yield of daily tasks
/// (GDD §5), and applies the resource side of event resolutions to a player.
///
/// <para>
/// The charging half exists so that a cost on a <c>LifeActivity</c> is real money. Until it did,
/// a 2 000-cost leisure day was free, which quietly removed the "spend" side of the progression
/// loop and made every priced activity strictly better than its free alternatives.
/// </para>
/// </summary>
public interface IEconomyService
{
    /// <summary>Yield for a task, scaled by the player's owned housing items/staff.</summary>
    TaskYield ComputeTaskYield(int playerId, string taskKey);

    /// <summary>Apply the resource deltas (money, etc.) from a resolved event.</summary>
    void ApplyResolution(int playerId, EventResolutionResult result);

    /// <summary>Current balance. Zero when the player has no finances row yet.</summary>
    long GetBalance(int playerId);

    /// <summary>True when the player can cover <paramref name="amount"/>.</summary>
    bool CanAfford(int playerId, long amount);

    /// <summary>
    /// Debit the player and write a ledger row. Returns false and changes nothing when the balance
    /// will not cover it — the caller decides whether that is a disabled button or a refusal.
    /// </summary>
    bool TryCharge(int playerId, long amount, string category, DateTime date);

    /// <summary>Credit the player and write a ledger row.</summary>
    void Credit(int playerId, long amount, string category, DateTime date);
}
