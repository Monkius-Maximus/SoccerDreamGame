namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Mutable physical state of the ball: position, linear velocity, and spin (angular velocity,
/// rad/s). A class (not a struct) because <see cref="BallPhysics.Step"/> mutates it in place each
/// tick and both the headless and rendered paths read the same instance after the tick.
/// </summary>
public sealed class BallState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Spin;

    public BallState(Vector3 position, Vector3 velocity, Vector3 spin)
    {
        Position = position;
        Velocity = velocity;
        Spin = spin;
    }

    /// <summary>True when the ball is resting on (or below) the pitch surface.</summary>
    public bool OnGround => Position.Y <= BallPhysics.Radius + 1e-6;
}
