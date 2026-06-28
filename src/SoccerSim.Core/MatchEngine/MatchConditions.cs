namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Environmental inputs to the match engine. <see cref="Wind"/> is added to the ball's
/// air-relative velocity before drag/Magnus; <see cref="RainIntensity"/> and
/// <see cref="PitchWear"/> (both 0..1) modulate the ground bounce and friction. Values are
/// placeholders to be calibrated by game feel, not measured physically.
/// </summary>
public readonly record struct MatchConditions(Vector3 Wind, double RainIntensity, double PitchWear)
{
    /// <summary>Calm air, dry weather, pristine pitch.</summary>
    public static MatchConditions Default => new(Vector3.Zero, 0.0, 0.0);
}
