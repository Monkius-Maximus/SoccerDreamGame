namespace SoccerSim.Core.LifeSim;

/// <summary>
/// Per-need tuning: how much it drains in a day and how much it counts toward the overall
/// wellbeing index.
/// </summary>
/// <param name="DailyDecay">Points lost per simulated day before cross-effects and variance.</param>
/// <param name="Weight">
/// Share of <see cref="WellbeingSnapshot.Index"/> this need carries. The weights of a profile sum
/// to 1.0, which is what makes the index directly comparable between the two roles.
/// </param>
public readonly record struct NeedTuning(double DailyDecay, double Weight);

/// <summary>
/// The role-dependent physics of the life simulation — and the ONLY thing that differs between a
/// player's off-pitch life and a manager's.
///
/// <para>
/// Both roles run the same six needs through the same <see cref="LifeSimulator"/>; this profile is
/// what makes an athlete's life revolve around <see cref="NeedKind.Fitness"/> while a manager's
/// revolves around <see cref="NeedKind.Focus"/> and <see cref="NeedKind.Social"/> (man-management).
/// Swapping the profile re-tunes the whole life-sim without touching a line of simulator code.
/// </para>
/// </summary>
public sealed class NeedProfile
{
    private readonly NeedTuning[] _tunings;

    private NeedProfile(CareerRole role, NeedTuning[] tunings)
    {
        Role = role;
        _tunings = tunings;
    }

    public CareerRole Role { get; }

    /// <summary>Tuning for a single need.</summary>
    public NeedTuning this[NeedKind need] => _tunings[(int)need];

    /// <summary>
    /// The canonical profile for a role. Weights sum to 1.0 in both, so a wellbeing index of 60
    /// means the same "how well is this person coping" regardless of which career is being played.
    /// </summary>
    public static NeedProfile For(CareerRole role) => role switch
    {
        CareerRole.Player => Player,
        CareerRole.Manager => Manager,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown career role."),
    };

    /// <summary>
    /// An athlete's life. The body is the axis everything else serves: <see cref="NeedKind.Fitness"/>
    /// and <see cref="NeedKind.MuscleCondition"/> together carry over a third of the index, and
    /// training/matches are what drain <see cref="NeedKind.Energy"/>, <see cref="NeedKind.Nutrition"/>
    /// and muscle freshness fastest.
    /// </summary>
    public static NeedProfile Player { get; } = new(CareerRole.Player, BuildOrThrow(new Dictionary<NeedKind, NeedTuning>
    {
        [NeedKind.Energy] = new(DailyDecay: 12.0, Weight: 0.16),
        [NeedKind.Nutrition] = new(DailyDecay: 10.0, Weight: 0.12),
        [NeedKind.Hygiene] = new(DailyDecay: 14.0, Weight: 0.06),
        [NeedKind.Fitness] = new(DailyDecay: 6.0, Weight: 0.20),
        [NeedKind.MuscleCondition] = new(DailyDecay: 9.0, Weight: 0.16),
        [NeedKind.Morale] = new(DailyDecay: 4.0, Weight: 0.12),
        [NeedKind.Social] = new(DailyDecay: 7.0, Weight: 0.08),
        [NeedKind.Focus] = new(DailyDecay: 8.0, Weight: 0.10),
    }));

    /// <summary>
    /// A manager's life. Tactical clarity and the dressing room are the axes: <see cref="NeedKind.Focus"/>
    /// carries the heaviest weight and drains fastest, <see cref="NeedKind.Social"/> matters more than
    /// double what it does for a player, and the two physical needs collapse to a background health
    /// signal rather than a performance input.
    /// </summary>
    public static NeedProfile Manager { get; } = new(CareerRole.Manager, BuildOrThrow(new Dictionary<NeedKind, NeedTuning>
    {
        [NeedKind.Energy] = new(DailyDecay: 14.0, Weight: 0.18),
        [NeedKind.Nutrition] = new(DailyDecay: 9.0, Weight: 0.09),
        [NeedKind.Hygiene] = new(DailyDecay: 12.0, Weight: 0.05),
        [NeedKind.Fitness] = new(DailyDecay: 3.0, Weight: 0.04),
        [NeedKind.MuscleCondition] = new(DailyDecay: 3.0, Weight: 0.03),
        [NeedKind.Morale] = new(DailyDecay: 5.0, Weight: 0.19),
        [NeedKind.Social] = new(DailyDecay: 6.0, Weight: 0.19),
        [NeedKind.Focus] = new(DailyDecay: 10.0, Weight: 0.23),
    }));

    /// <summary>
    /// Flattens the authored dictionary into a need-indexed array, failing fast if a need is missing
    /// or the weights do not sum to 1.0. A silently unnormalised profile would make the two roles'
    /// wellbeing indices incomparable, which is precisely the drift this system exists to prevent.
    /// </summary>
    private static NeedTuning[] BuildOrThrow(IReadOnlyDictionary<NeedKind, NeedTuning> authored)
    {
        var tunings = new NeedTuning[Needs.All.Length];
        double totalWeight = 0.0;

        foreach (NeedKind need in Needs.All)
        {
            if (!authored.TryGetValue(need, out NeedTuning tuning))
                throw new InvalidOperationException($"Need profile is missing a tuning for '{need}'.");
            if (tuning.DailyDecay < 0.0)
                throw new InvalidOperationException($"Need '{need}' has a negative daily decay.");
            if (tuning.Weight < 0.0)
                throw new InvalidOperationException($"Need '{need}' has a negative weight.");

            tunings[(int)need] = tuning;
            totalWeight += tuning.Weight;
        }

        if (Math.Abs(totalWeight - 1.0) > 1e-9)
            throw new InvalidOperationException($"Need profile weights must sum to 1.0, summed to {totalWeight}.");

        return tunings;
    }
}
