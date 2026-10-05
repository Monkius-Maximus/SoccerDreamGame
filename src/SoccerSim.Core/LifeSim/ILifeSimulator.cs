using SoccerSim.Core.Events;

namespace SoccerSim.Core.LifeSim;

/// <summary>A need that ended a simulated day in a degraded band; surfaced to the UI as a warning.</summary>
public readonly record struct NeedAlert(DateTime Date, NeedKind Need, NeedBand Band, double Value);

/// <summary>
/// What actually happened when an activity was performed. <see cref="AppliedDeltas"/> carries the
/// post-clamp changes rather than the activity's nominal ones, so the UI can honestly report "+12
/// Energy" when a nominal "+45" hit the 100 ceiling.
/// </summary>
public readonly record struct ActivityOutcome(
    string ActivityKey,
    IReadOnlyList<NeedDelta> AppliedDeltas,
    long Cost);

/// <summary>
/// Drives the off-pitch life simulation: daily need decay, the cross-effects between needs, and the
/// application of <see cref="LifeActivity"/> choices.
///
/// <para>
/// Engine-agnostic and role-agnostic. The same instance serves a <see cref="CareerRole.Player"/> and
/// a <see cref="CareerRole.Manager"/> career — the state's <see cref="NeedProfile"/> supplies the
/// role-dependent physics, so there is exactly one implementation to test and to keep balanced.
/// </para>
/// </summary>
public interface ILifeSimulator
{
    /// <summary>Derive the read model every other system consumes.</summary>
    WellbeingSnapshot Snapshot(WellbeingState state);

    /// <summary>
    /// Advance one simulated day: apply per-need decay (with seeded variance), then the cross-effect
    /// penalties from any need that bottomed out. Mutates <paramref name="state"/> and returns the
    /// needs that finished the day degraded. Wired to <c>ITimeManager.DayElapsed</c>.
    /// </summary>
    IReadOnlyList<NeedAlert> AdvanceDay(WellbeingState state, DateTime date, IRandom rng);

    /// <summary>
    /// Apply an activity's need deltas. Throws when the activity is not available to the state's
    /// role, so a manager can never be handed an athlete's gym session by a mis-wired UI.
    /// </summary>
    ActivityOutcome Perform(WellbeingState state, LifeActivity activity);
}
