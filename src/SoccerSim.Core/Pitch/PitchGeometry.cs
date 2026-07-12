namespace SoccerSim.Core.Pitch;

/// <summary>Which side of the pitch a player belongs to. Home attacks toward +X, Away toward −X.</summary>
public enum TeamSide
{
    Home,
    Away,
}

public static class TeamSideExtensions
{
    public static TeamSide Opponent(this TeamSide side) => side switch
    {
        TeamSide.Home => TeamSide.Away,
        TeamSide.Away => TeamSide.Home,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown team side."),
    };
}

/// <summary>
/// Pitch dimensions in metres, with the world coordinate frame used by the tick simulation:
/// origin at the Home team's left corner flag, X along the pitch length (0 = Home goal line,
/// Length = Away goal line), Y across the width.
/// </summary>
public sealed record PitchDimensions
{
    public PitchDimensions(double length, double width, double goalWidth)
    {
        if (length <= 0.0 || width <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(length), $"Pitch dimensions must be positive (got {length}x{width}).");
        if (goalWidth <= 0.0 || goalWidth >= width)
            throw new ArgumentOutOfRangeException(nameof(goalWidth), goalWidth, "Goal width must be positive and narrower than the pitch.");

        Length = length;
        Width = width;
        GoalWidth = goalWidth;
    }

    /// <summary>FIFA standard pitch: 105 x 68 m, 7.32 m goal mouth.</summary>
    public static PitchDimensions Standard { get; } = new(105.0, 68.0, 7.32);

    public double Length { get; }

    public double Width { get; }

    public double GoalWidth { get; }

    public Vec2 Centre => new(Length / 2.0, Width / 2.0);

    /// <summary>Centre of the goal the given side DEFENDS.</summary>
    public Vec2 DefendedGoal(TeamSide side)
        => side == TeamSide.Home ? new Vec2(0.0, Width / 2.0) : new Vec2(Length, Width / 2.0);

    /// <summary>Centre of the goal the given side ATTACKS.</summary>
    public Vec2 AttackedGoal(TeamSide side) => DefendedGoal(side.Opponent());

    /// <summary>Clamp a point into the field of play.</summary>
    public Vec2 Clamp(Vec2 point)
        => new(Math.Clamp(point.X, 0.0, Length), Math.Clamp(point.Y, 0.0, Width));

    /// <summary>
    /// Convert a side-relative normalised position (X: 0 = own goal line → 1 = opponent goal
    /// line, Y: 0..1 across the width from the own-goal perspective) into world coordinates.
    /// Both axes are mirrored for the Away side so one formation definition serves both teams.
    /// </summary>
    public Vec2 ToWorld(TeamSide side, Vec2 normalized)
    {
        if (normalized.X is < 0.0 or > 1.0 || normalized.Y is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(normalized), normalized, "Normalised pitch coordinates must be within [0, 1].");

        return side == TeamSide.Home
            ? new Vec2(normalized.X * Length, normalized.Y * Width)
            : new Vec2((1.0 - normalized.X) * Length, (1.0 - normalized.Y) * Width);
    }
}
