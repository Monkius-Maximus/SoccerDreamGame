namespace SoccerSim.Core.LifeSim;

/// <summary>
/// The eight off-pitch needs tracked for whichever career the human lives. Each is a 0–100 gauge
/// that drains daily and is topped up by <see cref="LifeActivity"/> choices.
///
/// <para>
/// The set is identical for <see cref="CareerRole.Player"/> and <see cref="CareerRole.Manager"/> on
/// purpose — a manager still sleeps, eats and needs company. What changes per role is the drain
/// rate and the weight each need carries, both of which live in <see cref="NeedProfile"/>.
/// </para>
///
/// <para>
/// <b>Reconciling the concepts.</b> Four different need lists existed across the design artifacts
/// (this simulation, the world HUD mockup, the phone's health app, and the reimagined prototype).
/// They were folded into this one set by four rules, so nothing is lost and nothing is duplicated:
/// </para>
///
/// <list type="bullet">
///   <item><b>Every gauge points the same way.</b> 100 is always good. The mockups showed "Fome"
///   (hunger) and "Dor Muscular" (muscle soreness), which are deficits that fill up as things get
///   worse. Modelled here as <see cref="Nutrition"/> and <see cref="MuscleCondition"/> so no gauge
///   has inverted polarity — a rule worth more than matching a mockup's noun.</item>
///
///   <item><b>"Sono" folded into <see cref="Energy"/>.</b> The phone mockup listed Energia and Sono
///   as separate bars, but both are answered by the same act (sleeping) and would move together
///   forever. Two gauges the player can never separate teach a decision that does not exist.</item>
///
///   <item><b>"Estresse" and "Injury Risk" are not needs.</b> They are consequences — you cannot
///   "top up" stress by resting a stress bar, you rest the things that cause it. Both are computed
///   in <see cref="WellbeingSnapshot"/> instead, where they read from several needs at once.</item>
///
///   <item><b>"Sharpness" is <see cref="Focus"/>.</b> Same concept, different word; reconciled to
///   one gauge with a per-locale label.</item>
/// </list>
///
/// <para>
/// Fame/reputation is also deliberately absent. It accumulates rather than draining toward a
/// deficit, so a bar that can never be topped up by resting would misteach the loop — the phone's
/// social-media app is where it belongs.
/// </para>
/// </summary>
public enum NeedKind
{
    /// <summary>Rest and sleep. Absorbs the mockups' separate "Sono" gauge.</summary>
    Energy,

    /// <summary>Diet quality ("Fome" inverted). Starves <see cref="Fitness"/> when it bottoms out.</summary>
    Nutrition,

    /// <summary>
    /// Personal upkeep. Cheap to satisfy and easy to neglect, which is exactly what makes it a good
    /// pressure on a packed schedule; bottoming out drags <see cref="Morale"/> down.
    /// </summary>
    Hygiene,

    /// <summary>Physical conditioning. Dominant for a player, near-cosmetic for a manager.</summary>
    Fitness,

    /// <summary>
    /// Freshness of the muscles ("Dor Muscular" inverted). Distinct from <see cref="Energy"/>: a
    /// player can be well rested and still sore, and it is soreness — not tiredness — that turns a
    /// heavy training week into a torn hamstring. The single biggest input to
    /// <see cref="WellbeingSnapshot.InjuryRisk"/> after conditioning.
    /// </summary>
    MuscleCondition,

    /// <summary>Mental state / happiness. Feeds form and raises the event-pressure multiplier.</summary>
    Morale,

    /// <summary>Relationships and company. Starves <see cref="Morale"/> when it bottoms out.</summary>
    Social,

    /// <summary>
    /// Mental sharpness (the prototype's "Sharpness"). Dominant for a manager — tactical clarity —
    /// and secondary for a player.
    /// </summary>
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
        NeedKind.Hygiene,
        NeedKind.Fitness,
        NeedKind.MuscleCondition,
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
