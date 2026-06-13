using SoccerSim.Core.Events;

namespace SoccerSim.Core.Economy;

/// <summary>The stat changes yielded by completing a simulated daily task.</summary>
public readonly record struct TaskYield(IReadOnlyList<StatDelta> StatDeltas);

/// <summary>
/// Computes income/expenditure and the housing-multiplied yield of daily tasks
/// (GDD §5), and applies the resource side of event resolutions to a player.
/// </summary>
public interface IEconomyService
{
    /// <summary>Yield for a task, scaled by the player's owned housing items/staff.</summary>
    TaskYield ComputeTaskYield(int playerId, string taskKey);

    /// <summary>Apply the resource deltas (money, etc.) from a resolved event.</summary>
    void ApplyResolution(int playerId, EventResolutionResult result);
}
