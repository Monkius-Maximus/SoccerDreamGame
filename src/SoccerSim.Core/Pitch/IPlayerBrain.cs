namespace SoccerSim.Core.Pitch;

/// <summary>The deciding player's team situation this tick.</summary>
public enum MatchPhase
{
    /// <summary>A teammate (or the player) owns the ball.</summary>
    InPossession,

    /// <summary>An opponent owns the ball.</summary>
    OutOfPossession,

    /// <summary>The ball is loose.</summary>
    Contested,
}

/// <summary>Read-only port onto the zonal influence map both AI layers consult.</summary>
public interface IInfluenceMap
{
    /// <summary>How strongly <paramref name="side"/> controls the space at <paramref name="point"/>, in [0, 1].</summary>
    double ControlAt(Vec2 point, TeamSide side);
}

/// <summary>
/// Everything one player perceives at a tick. <see cref="Teammates"/> excludes
/// <see cref="Self"/>; <see cref="Phase"/> is relative to the perceiving player's team.
/// </summary>
public sealed record PlayerPerception(
    PlayerState Self,
    BallState Ball,
    IReadOnlyList<PlayerState> Teammates,
    IReadOnlyList<PlayerState> Opponents,
    MatchPhase Phase,
    IInfluenceMap Influence,
    PitchDimensions Pitch);

/// <summary>
/// What a player wants to do this tick. Movement-only ticks yield <see cref="Move"/>;
/// on-ball actions yield <see cref="Pass"/> / <see cref="Shoot"/> / <see cref="Tackle"/>.
/// Dribbling and carrying are <see cref="Move"/> intentions while owning the ball —
/// the simulation keeps the ball glued to its owner.
/// </summary>
public abstract record Intention
{
    private Intention()
    {
    }

    /// <summary>Reposition (or carry/dribble the ball) with the given desired velocity in m/s.</summary>
    public sealed record Move(Vec2 DesiredVelocity) : Intention;

    /// <summary>Release the ball toward a teammate. <paramref name="TargetPosition"/> is the aimed point (may lead the runner).</summary>
    public sealed record Pass(int TargetPlayerId, Vec2 TargetPosition) : Intention;

    /// <summary>Strike at goal, aiming at <paramref name="Target"/>.</summary>
    public sealed record Shoot(Vec2 Target) : Intention;

    /// <summary>Attempt to dispossess the given opponent (only meaningful in tackling range).</summary>
    public sealed record Tackle(int TargetPlayerId) : Intention;

    /// <summary>Do nothing this tick.</summary>
    public sealed record Idle : Intention;
}

/// <summary>
/// The per-player decision seam of the tick-based match simulation: tactics, personality and
/// attributes go in at construction; one <see cref="Intention"/> comes out per tick.
/// </summary>
public interface IPlayerBrain
{
    Intention Decide(int tick, PlayerPerception perception);
}
