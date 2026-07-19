using SoccerSim.Core.Domain;

namespace SoccerSim.Core.Pitch;

/// <summary>Kinematic state of one player on the pitch at a tick (positions in metres, velocities in m/s).</summary>
public sealed record PlayerState(int PlayerId, TeamSide Side, Vec2 Position, Vec2 Velocity);

/// <summary>State of the ball at a tick. When owned, the ball travels glued to its owner.</summary>
public sealed record BallState(Vec2 Position, Vec2 Velocity, int? OwnerPlayerId)
{
    public bool IsLoose => OwnerPlayerId is null;
}

/// <summary>
/// The minimal personality dimensions the on-pitch AI consumes, each normalised to [0, 1].
/// TODO(Character Creation): the full Personality Vector lives in the character-creation
/// design docs and will replace/expand this stub; do not grow it here.
/// </summary>
public readonly record struct PersonalityProfile
{
    public PersonalityProfile(double aggression, double selfishness, double discipline)
    {
        Aggression = ValidateUnit(aggression, nameof(aggression));
        Selfishness = ValidateUnit(selfishness, nameof(selfishness));
        Discipline = ValidateUnit(discipline, nameof(discipline));
    }

    public double Aggression { get; }

    public double Selfishness { get; }

    public double Discipline { get; }

    public static PersonalityProfile Neutral => new(0.5, 0.5, 0.5);

    /// <summary>
    /// Derive the profile from the persisted trait catalogue (0..100 scales). Discipline has no
    /// trait dimension yet, so it defaults to neutral.
    /// TODO(Character Creation): map the full trait system onto the Personality Vector.
    /// </summary>
    public static PersonalityProfile FromTraits(IReadOnlyList<PlayerTrait> traits)
    {
        ArgumentNullException.ThrowIfNull(traits);
        if (traits.Count == 0)
            return Neutral;

        double aggression = traits.Average(t => t.Aggression) / 100.0;
        double selfishness = traits.Average(t => t.Selfishness) / 100.0;
        return new PersonalityProfile(aggression, selfishness, 0.5);
    }

    private static double ValidateUnit(double value, string name)
        => value is >= 0.0 and <= 1.0
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Personality dimensions must be within [0, 1].");
}

/// <summary>
/// A player as the on-pitch AI sees them: identity plus the attribute/personality subset the
/// utility considerations consume, normalised to [0, 1] from the persisted 1..20 domain scale.
/// TODO(Character Creation): the full attribute system (dedicated Finishing / Power / Dribbling
/// values on a wider scale) lives in its own design doc and will expand these derived views.
/// </summary>
public sealed record PitchPlayer(int PlayerId, PlayerAttributes Attributes, PersonalityProfile Personality)
{
    public double Finishing => Norm(Attributes.Shooting);

    public double Power => Norm(Attributes.Strength);

    public double PassingSkill => Norm(Attributes.Passing);

    public double VisionSkill => Norm(Attributes.Vision);

    public double TacklingSkill => Norm(Attributes.Tackling);

    public double PaceSkill => Norm(Attributes.Pace);

    /// <summary>No dedicated Dribbling attribute exists yet; approximated from pace + vision.</summary>
    public double DribblingSkill => Math.Clamp(((2.0 * Attributes.Pace) + Attributes.Vision - 3.0) / (3.0 * 19.0), 0.0, 1.0);

    /// <summary>Top running speed in m/s (4.0 for the slowest, 8.0 for the fastest player).</summary>
    public double MaxSpeed => 4.0 + (PaceSkill * 4.0);

    private static double Norm(int value) => Math.Clamp((value - 1) / 19.0, 0.0, 1.0);
}
