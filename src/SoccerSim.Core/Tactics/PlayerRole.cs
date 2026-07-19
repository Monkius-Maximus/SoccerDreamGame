using SoccerSim.Core.Pitch;

namespace SoccerSim.Core.Tactics;

/// <summary>
/// The lean MVP role set. Football Manager has dozens of specialised roles; deliberately NOT
/// replicated here — role nuance emerges from duty + instructions + personality instead.
/// </summary>
public enum PlayerRole
{
    Goalkeeper,
    Defender,
    Midfielder,
    Forward,
}

/// <summary>How a role leans within the team shape: sit deeper, hold, or push on.</summary>
public enum Duty
{
    Defend,
    Support,
    Attack,
}

/// <summary>
/// One positional slot of a formation. <see cref="BasePosition"/> is side-relative normalised
/// (X: 0 = own goal line → 1 = opponent goal line, Y: 0..1 across the width) and is the anchor
/// every steering blend returns to.
/// </summary>
public sealed record FormationSlot
{
    public FormationSlot(PlayerRole role, Duty duty, Vec2 basePosition)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown player role.");
        if (!Enum.IsDefined(duty))
            throw new ArgumentOutOfRangeException(nameof(duty), duty, "Unknown duty.");
        if (basePosition.X is < 0.0 or > 1.0 || basePosition.Y is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(basePosition), basePosition, "Formation slot positions are normalised and must be within [0, 1].");

        Role = role;
        Duty = duty;
        BasePosition = basePosition;
    }

    public PlayerRole Role { get; }

    public Duty Duty { get; }

    public Vec2 BasePosition { get; }
}
