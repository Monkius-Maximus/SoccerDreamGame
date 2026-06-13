namespace SoccerSim.Core.Domain;

public sealed class Sponsorship
{
    public int Id { get; init; }
    public int PlayerId { get; init; }
    public required string Sponsor { get; init; }
    public long WeeklyAmount { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// A purchasable housing/staff item. Owning one multiplies the stat-yield of the
/// matching daily simulated task (e.g. a better bed → faster stamina recovery), which
/// is the core progression loop of GDD §5.
/// </summary>
public sealed class HousingItem
{
    public int Id { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public long Cost { get; init; }

    /// <summary>The task/stat this item improves, e.g. "stamina_recovery".</summary>
    public required string StatKey { get; init; }

    /// <summary>Multiplier applied to that task's yield (1.0 = no bonus).</summary>
    public double YieldMultiplier { get; init; } = 1.0;
}
