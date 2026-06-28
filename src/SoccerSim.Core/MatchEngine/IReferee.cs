namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Read-only view of the match the referee may inspect. A <c>readonly record struct</c> built once
/// per tick. The referee reads it; it never mutates state.
/// </summary>
public readonly record struct MatchStateView(
    int Tick,
    int Minute,
    BallState Ball,
    IReadOnlyList<PlayerState> HomePlayers,
    IReadOnlyList<PlayerState> AwayPlayers,
    int? LastTouchPlayerId,
    int? LastTouchTeamId,
    int HomeGoals,
    int AwayGoals);

/// <summary>
/// Pure judgement over state the engine already computes (last touch, positions, contact). Emits
/// the judgement events (foul/card/penalty/offside). The real IFAB-driven referee plugs in here
/// without the engine changing — it is a function of state, not an agent that acts.
/// </summary>
public interface IReferee
{
    IEnumerable<MatchEvent> Evaluate(MatchStateView state);
}
