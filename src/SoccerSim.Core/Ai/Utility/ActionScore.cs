using SoccerSim.Core.Pitch;

namespace SoccerSim.Core.Ai.Utility;

/// <summary>The lean MVP action set the utility layer scores.</summary>
public enum PlayerAction
{
    ShortPass,
    LongPass,
    Shoot,
    Dribble,
    Carry,
    Tackle,
    Contain,
}

/// <summary>
/// A scored, fully-targeted action option. <see cref="Target"/> is the action's spatial goal
/// (pass/shot aim point, carry destination, contain spot); <see cref="TargetPlayerId"/> is set
/// for actions aimed at a player (passes, tackles).
/// </summary>
public sealed record ActionScore(PlayerAction Action, double Score, int? TargetPlayerId, Vec2 Target);
