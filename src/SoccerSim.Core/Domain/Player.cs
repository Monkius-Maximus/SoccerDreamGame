namespace SoccerSim.Core.Domain;

/// <summary>
/// Immutable base attributes (1–20). STATIC: assigned at generation and read-only
/// during a season. Effective in-match values are derived via <see cref="WithModifier"/>.
/// </summary>
public readonly record struct PlayerAttributes(
    int Pace,
    int Stamina,
    int Strength,
    int Passing,
    int Shooting,
    int Tackling,
    int Vision)
{
    /// <summary>
    /// Inclusive bounds of every base attribute. Named constants rather than inline literals
    /// because three places must agree: this clamp, the <c>CHECK</c> constraints in
    /// <c>sql/0001_initial_schema.sql</c>, and the authoring-time content validator. Drift
    /// between them surfaces as an opaque constraint violation at import time.
    /// </summary>
    public const int MinValue = 1;

    public const int MaxValue = 20;

    /// <summary>Apply a FormMood modifier to every attribute, clamped to the 1–20 range.</summary>
    public PlayerAttributes WithModifier(FormMood mood)
    {
        static int Apply(int value, int delta) => Math.Clamp(value + delta, MinValue, MaxValue);
        int m = mood.Value;
        return new PlayerAttributes(
            Apply(Pace, m),
            Apply(Stamina, m),
            Apply(Strength, m),
            Apply(Passing, m),
            Apply(Shooting, m),
            Apply(Tackling, m),
            Apply(Vision, m));
    }
}

/// <summary>
/// DYNAMIC form/mood modifier in the range −5..+5. Recalculated post-match/event,
/// reset seasonally, and bypassed entirely in arcade modes.
/// </summary>
public readonly record struct FormMood(int Value)
{
    public static FormMood Neutral => new(0);

    public FormMood Adjust(int delta) => new(Math.Clamp(Value + delta, -5, 5));
}

/// <summary>
/// STATIC personality trait. Dictates off-pitch event probability weights AND
/// on-pitch AI behaviour (aggression, selfishness). Read-only during the season.
/// </summary>
public sealed record PlayerTrait(
    int Id,
    string Key,
    string DisplayName,
    int Aggression,
    int Selfishness,
    int EventWeightBias);

/// <summary>A player: static identity/attributes/traits plus a dynamic form/mood.</summary>
public sealed class Player
{
    public int Id { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public int? TeamId { get; set; }

    /// <summary>STATIC base attributes; read-only during the season.</summary>
    public PlayerAttributes BaseAttributes { get; init; }

    /// <summary>STATIC personality traits assigned at generation.</summary>
    public IReadOnlyList<PlayerTrait> Traits { get; init; } = [];

    /// <summary>DYNAMIC modifier; recalculated post-match/event and reset seasonally.</summary>
    public FormMood FormMood { get; set; } = FormMood.Neutral;

    /// <summary>
    /// Effective attributes for simulation. Arcade modes pass
    /// <paramref name="applyForm"/> = false to bypass the form/mood layer.
    /// </summary>
    public PlayerAttributes EffectiveAttributes(bool applyForm = true)
        => applyForm ? BaseAttributes.WithModifier(FormMood) : BaseAttributes;
}
