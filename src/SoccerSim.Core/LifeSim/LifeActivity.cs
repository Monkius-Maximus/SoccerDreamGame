using SoccerSim.Core.Localization;
using SoccerSim.Core.World;

namespace SoccerSim.Core.LifeSim;

/// <summary>A single need change applied by an activity, e.g. <c>(Energy, +45)</c>.</summary>
public readonly record struct NeedDelta(NeedKind Need, double Delta);

/// <summary>
/// Something the human spends off-pitch hours doing. Every activity trades one need against
/// another — that trade IS the life-sim's decision space, so no activity is purely positive.
/// </summary>
/// <param name="Key">Stable identifier. Also the activity-log key and the root of its string keys.</param>
/// <param name="DurationHours">In-game hours consumed, before travel.</param>
/// <param name="NeedDeltas">The need changes applied on completion.</param>
public sealed record LifeActivity(
    string Key,
    double DurationHours,
    IReadOnlyList<NeedDelta> NeedDeltas)
{
    /// <summary>Money spent performing it; settled through the economy's transaction ledger.</summary>
    public long Cost { get; init; }

    /// <summary>Which career roles may perform it. Defaults to the shared spine.</summary>
    public CareerRoles AvailableTo { get; init; } = CareerRoles.Both;

    /// <summary>
    /// The kind of venue this needs. Any location of that category can host it, which is what lets
    /// an imported world work without re-authoring activities per place.
    /// </summary>
    public VenueCategory RequiredVenue { get; init; } = VenueCategory.None;

    /// <summary>
    /// Localisation key for the display name. The simulation never holds a sentence — this is
    /// resolved by <see cref="ILocalizer"/> at the moment of drawing.
    /// </summary>
    public string NameKey => LocKeys.ActivityName(Key);

    /// <summary>Localisation key for the one-line description.</summary>
    public string DescriptionKey => LocKeys.ActivityDescription(Key);

    /// <summary>True when <paramref name="role"/> is permitted to perform this activity.</summary>
    public bool IsAvailableTo(CareerRole role) => (AvailableTo & ToFlag(role)) != CareerRoles.None;

    /// <summary>True when <paramref name="location"/> can host this activity.</summary>
    public bool CanBePerformedAt(WorldLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return RequiredVenue == VenueCategory.None || location.Category == RequiredVenue;
    }

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
///
/// <para>
/// Note how training is priced. It buys conditioning with rest, food, <b>muscle freshness and
/// hygiene</b> — so a player who trains every day walks into the weekend sore and filthy, and
/// soreness is the largest single input to injury risk after conditioning itself. That loop is the
/// reason <see cref="NeedKind.MuscleCondition"/> exists as a need separate from
/// <see cref="NeedKind.Energy"/>.
/// </para>
/// </summary>
public static class LifeActivityCatalogue
{
    /// <summary>Every activity, shared spine first, then the player and manager leaves.</summary>
    public static IReadOnlyList<LifeActivity> All { get; } =
    [
        // ── Shared spine ────────────────────────────────────────────────────────────────
        new("sleep", 8.0,
        [
            new(NeedKind.Energy, +45), new(NeedKind.Focus, +15),
            new(NeedKind.MuscleCondition, +12), new(NeedKind.Social, -5),
        ]) { RequiredVenue = VenueCategory.Home },

        // Cheap, fast, and easy to skip when the week is packed — which is exactly what makes
        // Hygiene a real pressure rather than a formality.
        new("shower", 0.5,
        [
            new(NeedKind.Hygiene, +55), new(NeedKind.Morale, +3),
        ]) { RequiredVenue = VenueCategory.Home },

        new("meal", 1.0,
        [
            new(NeedKind.Nutrition, +40), new(NeedKind.Energy, +5), new(NeedKind.Morale, +3),
        ]) { Cost = 150, RequiredVenue = VenueCategory.Dining },

        new("physio", 2.0,
        [
            new(NeedKind.MuscleCondition, +30), new(NeedKind.Fitness, +5), new(NeedKind.Energy, -5),
        ]) { Cost = 1_200, RequiredVenue = VenueCategory.Medical },

        new("socialise", 3.0,
        [
            new(NeedKind.Social, +35), new(NeedKind.Morale, +15),
            new(NeedKind.Energy, -10), new(NeedKind.Hygiene, -8),
        ]) { RequiredVenue = VenueCategory.Social },

        new("family_time", 4.0,
        [
            new(NeedKind.Social, +25), new(NeedKind.Morale, +20), new(NeedKind.Energy, -5),
        ]) { RequiredVenue = VenueCategory.Home },

        new("leisure", 3.0,
        [
            new(NeedKind.Morale, +25), new(NeedKind.Social, +10), new(NeedKind.Energy, -5),
        ]) { Cost = 2_000, RequiredVenue = VenueCategory.Commerce },

        // An obligation, not a treat: it costs on every axis. Included so the schedule has a cost
        // the human does not get to opt out of.
        new("media_duty", 2.0,
        [
            new(NeedKind.Focus, -8), new(NeedKind.Morale, -5),
            new(NeedKind.Energy, -8), new(NeedKind.Hygiene, -5),
        ]) { RequiredVenue = VenueCategory.Media },

        // ── Player-only leaves ──────────────────────────────────────────────────────────
        new("individual_training", 3.0,
        [
            new(NeedKind.Fitness, +18), new(NeedKind.Focus, +5),
            new(NeedKind.Energy, -20), new(NeedKind.Nutrition, -10),
            new(NeedKind.MuscleCondition, -22), new(NeedKind.Hygiene, -25),
        ]) { AvailableTo = CareerRoles.Player, RequiredVenue = VenueCategory.TrainingGround },

        new("gym_session", 2.0,
        [
            new(NeedKind.Fitness, +12), new(NeedKind.Energy, -15), new(NeedKind.Nutrition, -8),
            new(NeedKind.MuscleCondition, -18), new(NeedKind.Hygiene, -20),
        ]) { AvailableTo = CareerRoles.Player, RequiredVenue = VenueCategory.Gym },

        // ── Manager-only leaves ─────────────────────────────────────────────────────────
        new("film_study", 3.0,
        [
            new(NeedKind.Focus, +25), new(NeedKind.Energy, -12), new(NeedKind.Social, -8),
        ]) { AvailableTo = CareerRoles.Manager, RequiredVenue = VenueCategory.Home },

        new("staff_meeting", 2.0,
        [
            new(NeedKind.Focus, +10), new(NeedKind.Social, +12), new(NeedKind.Energy, -8),
        ]) { AvailableTo = CareerRoles.Manager, RequiredVenue = VenueCategory.TrainingGround },

        new("scouting_trip", 8.0,
        [
            new(NeedKind.Focus, +8), new(NeedKind.Morale, +5), new(NeedKind.Energy, -25),
            new(NeedKind.Social, -10), new(NeedKind.Hygiene, -15),
        ]) { AvailableTo = CareerRoles.Manager, RequiredVenue = VenueCategory.Stadium, Cost = 5_000 },
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

    /// <summary>
    /// Every activity the role may perform AND the venue can host — what the action bar shows when
    /// the human is standing somewhere specific.
    /// </summary>
    public static IReadOnlyList<LifeActivity> AvailableAt(CareerRole role, WorldLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return All
            .Where(activity => activity.IsAvailableTo(role) && activity.CanBePerformedAt(location))
            .ToArray();
    }
}
