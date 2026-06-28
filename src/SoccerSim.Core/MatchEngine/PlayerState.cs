using SoccerSim.Core.Domain;

namespace SoccerSim.Core.MatchEngine;

/// <summary>Mutable per-player state inside a running match. Position/Velocity live on the pitch plane (Y = 0).</summary>
public sealed class PlayerState
{
    public required int PlayerId { get; init; }

    public required int TeamId { get; init; }

    public PlayerAttributes Attributes { get; init; }

    public bool IsGoalkeeper { get; init; }

    /// <summary>The formation slot this player returns to when not chasing the ball.</summary>
    public Vector3 HomePosition { get; init; }

    public Vector3 Position;

    public Vector3 Velocity;

    /// <summary>Top running speed (m/s), scaled from the Pace attribute (1..20) — roughly 4..8 m/s.</summary>
    public double MaxSpeed => 4.0 + (Attributes.Pace / 20.0 * 4.0);
}
