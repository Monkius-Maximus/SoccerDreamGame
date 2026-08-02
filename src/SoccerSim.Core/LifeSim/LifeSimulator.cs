using SoccerSim.Core.Events;

namespace SoccerSim.Core.LifeSim;

/// <summary>
/// The one implementation of <see cref="ILifeSimulator"/>, shared by both career roles.
///
/// <para>
/// A day's advance is two ordered passes. First, every need decays by its
/// <see cref="NeedTuning.DailyDecay"/> scaled by a seeded variance band. Second, the cross-effects
/// fire: a need that had already bottomed out drags a dependent need down with it. Both passes read
/// the bands captured BEFORE any decay was written, so the outcome does not depend on the order the
/// needs happen to be iterated in — which is what keeps a save replaying identically from its seed.
/// </para>
/// </summary>
public sealed class LifeSimulator : ILifeSimulator
{
    /// <summary>
    /// Fraction by which a day's decay may swing either side of its nominal rate. A day is never
    /// exactly like the last, but the swing is drawn from the seeded stream so it stays reproducible.
    /// </summary>
    public const double DailyVarianceBand = 0.20;

    /// <summary>
    /// How much extra decay a dependent need takes when its driver has bottomed out, as a multiple
    /// of the dependent need's own daily rate. Eating badly costs conditioning; sleeplessness costs
    /// clarity; isolation costs mood.
    /// </summary>
    public const double CrossEffectMultiplier = 1.5;

    /// <summary>
    /// The cross-effect graph: when <c>Driver</c> is <see cref="NeedBand.Critical"/>, <c>Dependent</c>
    /// takes <see cref="CrossEffectMultiplier"/> extra days' worth of its own decay.
    ///
    /// <para>
    /// This graph is what stops the needs from being eight independent bars the player services in
    /// isolation: neglecting one makes another cheaper to lose, so a bad week compounds. Several
    /// drivers may feed the same dependent — each fires on its own.
    /// </para>
    /// </summary>
    private static readonly (NeedKind Driver, NeedKind Dependent)[] CrossEffects =
    [
        (NeedKind.Nutrition, NeedKind.Fitness),         // eating badly costs conditioning
        (NeedKind.Energy, NeedKind.Focus),              // sleeplessness costs clarity
        (NeedKind.Social, NeedKind.Morale),             // isolation costs mood
        (NeedKind.Hygiene, NeedKind.Morale),            // letting yourself go costs mood
        (NeedKind.MuscleCondition, NeedKind.Fitness),   // you cannot train through soreness
    ];

    public WellbeingSnapshot Snapshot(WellbeingState state) => WellbeingSnapshot.From(state);

    public IReadOnlyList<NeedAlert> AdvanceDay(WellbeingState state, DateTime date, IRandom rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);

        // Capture the starting bands first: cross-effects must judge the day the human woke up to,
        // not a half-decayed intermediate that would depend on iteration order.
        Span<NeedBand> bandsAtDayStart = stackalloc NeedBand[Needs.All.Length];
        foreach (NeedKind need in Needs.All)
            bandsAtDayStart[(int)need] = state.BandOf(need);

        // Pass 1 — nominal decay with seeded variance. One draw per need, in a fixed order.
        foreach (NeedKind need in Needs.All)
        {
            double variance = (rng.NextDouble() - 0.5) * 2.0 * DailyVarianceBand;
            state.Adjust(need, -state.Profile[need].DailyDecay * (1.0 + variance));
        }

        // Pass 2 — cross-effects from needs that were already critical at the start of the day.
        foreach ((NeedKind driver, NeedKind dependent) in CrossEffects)
        {
            if (bandsAtDayStart[(int)driver] == NeedBand.Critical)
                state.Adjust(dependent, -state.Profile[dependent].DailyDecay * CrossEffectMultiplier);
        }

        List<NeedAlert>? alerts = null;
        foreach (NeedKind need in Needs.All)
        {
            NeedBand band = state.BandOf(need);
            if (band is NeedBand.Low or NeedBand.Critical)
                (alerts ??= new List<NeedAlert>()).Add(new NeedAlert(date, need, band, state[need]));
        }

        return (IReadOnlyList<NeedAlert>?)alerts ?? Array.Empty<NeedAlert>();
    }

    public ActivityOutcome Perform(WellbeingState state, LifeActivity activity)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(activity);

        if (!activity.IsAvailableTo(state.Role))
        {
            throw new InvalidOperationException(
                $"Activity '{activity.Key}' is not available to a {state.Role} career.");
        }

        var applied = new List<NeedDelta>(activity.NeedDeltas.Count);
        foreach (NeedDelta delta in activity.NeedDeltas)
        {
            double before = state[delta.Need];
            state.Adjust(delta.Need, delta.Delta);
            double actual = state[delta.Need] - before;
            if (actual != 0.0)
                applied.Add(new NeedDelta(delta.Need, actual));
        }

        return new ActivityOutcome(activity.Key, applied, activity.Cost);
    }
}
