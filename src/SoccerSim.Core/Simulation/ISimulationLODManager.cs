namespace SoccerSim.Core.Simulation;

/// <summary>
/// Consumes advancing time from the <see cref="Time.ITimeManager"/> and resolves
/// each league according to its tier, keeping background processing cheap (GDD §6).
/// </summary>
public interface ISimulationLODManager
{
    SimulationTier GetTier(int leagueId);

    /// <summary>Driven by <c>ITimeManager.DayElapsed</c>; resolves Tier 1 fixtures due today.</summary>
    void OnDayElapsed(DateTime date);

    /// <summary>Driven by <c>ITimeManager.WeekElapsed</c>; resolves Tier 2 + Tier 3 fixtures.</summary>
    void OnWeekElapsed(DateTime weekEnd);

    /// <summary>Resolve a single known match (e.g. the player's own fixture) on demand.</summary>
    MatchResult ResolveMatch(int matchId, SimulationTier tier);
}
