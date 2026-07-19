using SoccerSim.Core.Pitch;
using SoccerSim.Core.Tactics;

namespace SoccerSim.Core.Ai;

/// <summary>
/// Computes a player's current formation anchor: the slot's base position, advanced by
/// mentality/duty, stretched by the width instruction, shifted with the phase of play, and
/// swayed toward the ball. This is the point every steering blend arrives back to.
/// </summary>
public static class TacticalAnchor
{
    /// <summary>Max metres the anchor may drift toward the ball, so shape never collapses onto it.</summary>
    private const double MaxBallSwayMetres = 12.0;

    /// <summary>Metres the whole line steps up when in possession / drops when out of it.</summary>
    private const double InPossessionShift = 6.0;
    private const double OutOfPossessionShift = -3.5;

    public static Vec2 Compute(
        FormationSlot slot,
        BehaviourWeights weights,
        TeamSide side,
        Vec2 ballPosition,
        MatchPhase phase,
        PitchDimensions pitch)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(pitch);

        double phaseShift = phase switch
        {
            MatchPhase.InPossession => InPossessionShift,
            MatchPhase.OutOfPossession => OutOfPossessionShift,
            MatchPhase.Contested => 0.0,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown match phase."),
        };

        double advance = weights.AnchorAdvanceMetres + (phaseShift * weights.PhaseShiftScale);
        double nx = Math.Clamp(slot.BasePosition.X + (advance / pitch.Length), 0.02, 0.98);
        double ny = Math.Clamp(0.5 + ((slot.BasePosition.Y - 0.5) * weights.WidthScale), 0.02, 0.98);

        Vec2 anchor = pitch.ToWorld(side, new Vec2(nx, ny));
        double swayFactor = weights.BallSway * (phase == MatchPhase.OutOfPossession ? weights.OutOfPossessionSwayBoost : 1.0);
        Vec2 sway = ((ballPosition - anchor) * swayFactor).ClampLength(MaxBallSwayMetres);
        return pitch.Clamp(anchor + sway);
    }
}
