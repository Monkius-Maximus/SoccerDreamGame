namespace SoccerSim.Core.LifeSim;

/// <summary>A single need change applied by an activity, e.g. <c>(Energy, +45)</c>.</summary>
public readonly record struct NeedDelta(NeedKind Need, double Delta);

/// <summary>
/// Something the human spends off-pitch hours doing. Every activity trades one need against
/// another — that trade IS the life-sim's decision space, so no activity is purely positive.
/// </summary>
/// <param name="Key">Stable identifier; also the key written to the activity log.</param>
/// <param name="DisplayName">Human-readable label for the UI.</param>
/// <param name="DurationHours">In-game hours consumed, for the daily schedule.</param>
/// <param name="NeedDeltas">The need changes applied on completion.</param>
public sealed record LifeActivity(
    string Key,
    string DisplayName,
    double DurationHours,
    IReadOnlyList<NeedDelta> NeedDeltas)
{
    /// <summary>Money spent performing it; settled through the economy's transaction ledger.</summary>
    public long Cost { get; init; }

    /// <summary>Which career roles may perform it. Defaults to the shared spine.</summary>
    public CareerRoles AvailableTo { get; init; } = CareerRoles.Both;

    /// <summary>True when <paramref name="role"/> is permitted to perform this activity.</summary>
    public bool IsAvailableTo(CareerRole role) => (AvailableTo & ToFlag(role)) != CareerRoles.None;

    internal static CareerRoles ToFlag(CareerRole role) => role switch
    {
        CareerRole.Player => CareerRoles.Player,
        CareerRole.Manager => CareerRoles.Manager,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown career role."),
    };
}

/// <summary>
/// The built-in activity catalogue: a shared spine both careers use, plus role-exclusive leaves.
///
/// <para>
/// The split is the point. Sleeping, eating and seeing people are the same act whoever you are, so
/// they live once and are reused; what a player does with a free afternoon (gym) and what a manager
/// does with it (film study) differ, so those are gated by <see cref="LifeActivity.AvailableTo"/>.
/// One catalogue, one lookup, no duplicated tuning.
/// </para>
/// </summary>
public static class LifeActivityCatalogue
{
    /// <summary>Every activity, shared spine first, then the player and manager leaves.</summary>
    public static IReadOnlyList<LifeActivity> All { get; } =
    [
        // ── Shared spine ────────────────────────────────────────────────────────────────
        new("sleep", "Sleep", 8.0,
        [
            new(NeedKind.Energy, +45), new(NeedKind.Focus, +15),
            new(NeedKind.Fitness, +2), new(NeedKind.Social, -5),
        ]),
        new("meal", "Proper Meal", 1.0,
        [
            new(NeedKind.Nutrition, +40), new(NeedKind.Energy, +5), new(NeedKind.Morale, +3),
        ]) { Cost = 150 },
        new("physio", "Physio Session", 2.0,
        [
            new(NeedKind.Fitness, +15), new(NeedKind.Energy, -5),
        ]) { Cost = 1_200 },
        new("socialise", "See Friends", 3.0,
        [
            new(NeedKind.Social, +35), new(NeedKind.Morale, +15), new(NeedKind.Energy, -10),
        ]),
        new("family_time", "Family Time", 4.0,
        [
            new(NeedKind.Social, +25), new(NeedKind.Morale, +20), new(NeedKind.Energy, -5),
        ]),
        new("leisure", "Leisure & Downtime", 3.0,
        [
            new(NeedKind.Morale, +25), new(NeedKind.Social, +10), new(NeedKind.Energy, -5),
        ]) { Cost = 2_000 },
        // An obligation, not a treat: it costs on every axis. Included so the schedule has a cost
        // the human does not get to opt out of.
        new("media_duty", "Media Duty", 2.0,
        [
            new(NeedKind.Focus, -8), new(NeedKind.Morale, -5), new(NeedKind.Energy, -8),
        ]),

        // ── Player-only leaves ──────────────────────────────────────────────────────────
        new("individual_training", "Individual Training", 3.0,
        [
            new(NeedKind.Fitness, +18), new(NeedKind.Focus, +5),
            new(NeedKind.Energy, -20), new(NeedKind.Nutrition, -10),
        ]) { AvailableTo = CareerRoles.Player },
        new("gym_session", "Gym Session", 2.0,
        [
            new(NeedKind.Fitness, +12), new(NeedKind.Energy, -15), new(NeedKind.Nutrition, -8),
        ]) { AvailableTo = CareerRoles.Player },

        // ── Manager-only leaves ─────────────────────────────────────────────────────────
        new("film_study", "Opposition Film Study", 3.0,
        [
            new(NeedKind.Focus, +25), new(NeedKind.Energy, -12), new(NeedKind.Social, -8),
        ]) { AvailableTo = CareerRoles.Manager },
        new("staff_meeting", "Staff Meeting", 2.0,
        [
            new(NeedKind.Focus, +10), new(NeedKind.Social, +12), new(NeedKind.Energy, -8),
        ]) { AvailableTo = CareerRoles.Manager },
        new("scouting_trip", "Scouting Trip", 8.0,
        [
            new(NeedKind.Focus, +8), new(NeedKind.Morale, +5),
            new(NeedKind.Energy, -25), new(NeedKind.Social, -10),
        ]) { AvailableTo = CareerRoles.Manager, Cost = 5_000 },
    ];

    private static readonly Dictionary<string, LifeActivity> ByKeyLookup =
        All.ToDictionary(activity => activity.Key, StringComparer.Ordinal);

    /// <summary>Look up an activity by key. Throws when the key is unknown (fail-fast).</summary>
    public static LifeActivity ByKey(string key) =>
        ByKeyLookup.TryGetValue(key, out LifeActivity? activity)
            ? activity
            : throw new KeyNotFoundException($"Unknown life activity '{key}'.");

    /// <summary>Every activity a given role is allowed to perform, in catalogue order.</summary>
    public static IReadOnlyList<LifeActivity> For(CareerRole role) =>
        All.Where(activity => activity.IsAvailableTo(role)).ToArray();
}
