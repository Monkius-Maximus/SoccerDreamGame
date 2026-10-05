namespace SoccerSim.Core.LifeSim;

/// <summary>
/// The six off-pitch needs tracked for whichever career the human lives. Each is a 0–100 gauge
/// that drains daily and is topped up by <see cref="LifeActivity"/> choices.
///
/// <para>
/// The set is identical for <see cref="CareerRole.Player"/> and <see cref="CareerRole.Manager"/>
/// on purpose — a manager still sleeps, eats, and needs company. What changes per role is the
/// drain rate and the weight each need carries, both of which live in <see cref="NeedProfile"/>.
/// </para>
///
/// <para>
/// Note what is deliberately NOT here: fame/reputation. Reputation is a standing you accumulate,
/// not a gauge that drains toward a deficit, so modelling it as a need would mean a bar that can
/// never be "topped up" by resting. It belongs with the economy/social standing systems instead.
/// </para>
/// </summary>
public enum NeedKind
{
    /// <summary>Rest and sleep. The fastest-draining need for both roles.</summary>
    Energy,

    /// <summary>Diet quality. Starves <see cref="Fitness"/> when it bottoms out.</summary>
    Nutrition,

    /// <summary>Physical conditioning. Dominant for a player, near-cosmetic for a manager.</summary>
    Fitness,

    /// <summary>Mental state / happiness. Feeds form and raises the event-pressure multiplier.</summary>
    Morale,

    /// <summary>Relationships and company. Starves <see cref="Morale"/> when it bottoms out.</summary>
    Social,

    /// <summary>Mental sharpness. Dominant for a manager (tactical clarity), secondary for a player.</summary>
    Focus,
}

/// <summary>
/// Qualitative band of a need value. Drives the UI colour ramp and the cross-effect penalties in
/// <see cref="LifeSimulator.AdvanceDay"/>, so both the visual and the mechanical thresholds come
/// from one place rather than drifting apart.
/// </summary>
public enum NeedBand
{
    /// <summary>Below 20 — actively harmful; triggers cross-effect drain on a dependent need.</summary>
    Critical,

    /// <summary>Below 40 — degrading; surfaces as a warning.</summary>
    Low,

    /// <summary>Below 75 — functional.</summary>
    Adequate,

    /// <summary>75 and above — thriving.</summary>
    Good,
}

/// <summary>Ordering and banding helpers for <see cref="NeedKind"/>.</summary>
public static class Needs
{
    /// <summary>Threshold below which a need is <see cref="NeedBand.Critical"/>.</summary>
    public const double CriticalThreshold = 20.0;

    /// <summary>Threshold below which a need is <see cref="NeedBand.Low"/>.</summary>
    public const double LowThreshold = 40.0;

    /// <summary>Threshold at or above which a need is <see cref="NeedBand.Good"/>.</summary>
    public const double GoodThreshold = 75.0;

    /// <summary>
    /// Every need in declaration order. Iterating this (rather than <c>Enum.GetValues</c> per call)
    /// keeps the daily advance allocation-free and its ordering stable, which matters because the
    /// simulation must replay identically from a seed.
    /// </summary>
    public static readonly NeedKind[] All =
    [
        NeedKind.Energy,
        NeedKind.Nutrition,
        NeedKind.Fitness,
        NeedKind.Morale,
        NeedKind.Social,
        NeedKind.Focus,
    ];

    /// <summary>The band a raw 0–100 value falls into.</summary>
    public static NeedBand BandOf(double value) => value switch
    {
        < CriticalThreshold => NeedBand.Critical,
        < LowThreshold => NeedBand.Low,
        < GoodThreshold => NeedBand.Adequate,
        _ => NeedBand.Good,
    };
}
