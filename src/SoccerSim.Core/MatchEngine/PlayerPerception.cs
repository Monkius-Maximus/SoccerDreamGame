namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// The typed slice of match state a brain may read when deciding. Built by
/// <see cref="MatchSimulation"/> each tick. A <c>readonly record struct</c> so perceiving 22 players
/// every tick allocates nothing on the heap. The teammate/opponent lists are the engine's live
/// player arrays (shared references) — read them, don't mutate them.
/// </summary>
public readonly record struct PlayerPerception(
    PlayerState Self,
    Vector3 BallPosition,
    Vector3 BallVelocity,
    IReadOnlyList<PlayerState> Teammates,
    IReadOnlyList<PlayerState> Opponents,
    Vector3 AttackingGoalCentre,
    Vector3 OwnGoalCentre);
