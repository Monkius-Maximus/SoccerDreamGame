namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// A player's decision for a single tick. A closed hierarchy (abstract record + sealed subtypes)
/// so the real steering/utility brain can add cases, while the engine dispatches on a small set.
/// </summary>
public abstract record Intention
{
    // Private ctor closes the hierarchy to the nested subtypes below.
    private Intention() { }

    /// <summary>Hold position / decelerate.</summary>
    public sealed record Idle : Intention;

    /// <summary>Run in a direction on the pitch plane (the engine normalises it).</summary>
    public sealed record Move(Vector3 Direction) : Intention;

    /// <summary>Pass to a teammate — honoured only when the player controls the ball.</summary>
    public sealed record Pass(int TargetPlayerId) : Intention;

    /// <summary>Strike the ball in a direction with power and spin — honoured only when controlling it.</summary>
    public sealed record Shoot(Vector3 Direction, double Power, Vector3 Spin) : Intention;

    /// <summary>Attempt to win the ball (the contact is judged by the referee module later).</summary>
    public sealed record Tackle : Intention;
}
