using SoccerSim.Core.World.Color;
using SoccerSim.Core.World.Economy;

namespace SoccerSim.Core.World;

/// <summary>
/// The single point where derived fields are recomputed from authored ones. Every write path
/// (importer, repositories) runs its record through here first, so a derived value can never
/// be persisted stale or accepted from a client (ROADMAP.md Sprint 2: "Nunca aceitar derivado
/// vindo do cliente").
///
/// <para>
/// What is derived is exactly the list in ALGORITHMS.md §4.3 and §2 — no more. In particular
/// <c>Age</c> and <c>Phase</c> are NOT derived here even though the data contract marks them
/// derived: 297 of the 688 source players disagree with a birthday-aware age at the contract's
/// reference date, and age feeds the value curve. Recomputing them would rewrite those players'
/// economy on the first save. Widening this list needs the same evidence Sprint 1 produced —
/// run the whole batch through it and check nothing moves.
/// </para>
/// </summary>
public static class WorldDerivations
{
    public const string PolarityDarkHome = "titular escura -> reserva no polo claro da palette";
    public const string PolarityLightHome = "titular clara -> reserva no polo escuro da palette";

    /// <summary>Luminance below this makes the home shirt "dark" for kit polarity purposes.</summary>
    private const double DarkHomeLuminanceThreshold = 0.35;

    /// <summary>
    /// Recomputes a club's derived fields: crest colors mirror the palette, ΔE and home
    /// luminance come from the kits, the polarity rule from that luminance, and the home
    /// advantage modifier from the stadium's atmosphere via calibration.
    /// </summary>
    public static ClubIdentity Recalculate(ClubIdentity club, WorldCalibration calibration)
    {
        double luminance = Round(ColorMath.RelativeLuminance(club.Kits.Home.Shirt), 4);
        double deltaE = Round(ColorMath.DeltaE76(club.Kits.Home.Shirt, club.Kits.Away.Shirt), 1);

        return club with
        {
            Crest = club.Crest with
            {
                Colors = [club.Palette.Primary, club.Palette.Secondary, club.Palette.Tertiary],
            },
            Kits = club.Kits with
            {
                Home = club.Kits.Home with { Luminance = luminance },
                DeltaE = deltaE,
                PolarityRule = luminance < DarkHomeLuminanceThreshold ? PolarityDarkHome : PolarityLightHome,
            },
            AiProfile = club.AiProfile with
            {
                HomeAdvantageModifier = calibration.HomeAdv[club.Stadium.AtmosphereArchetype],
            },
        };
    }

    /// <summary>
    /// Recomputes a character's derived fields: shirt name, overall from the position weights,
    /// and the economy chain (potential, market value, salary). <paramref name="band"/> is the
    /// prestige band of the character's club, which scales market value.
    /// </summary>
    public static CharacterRecord Recalculate(CharacterRecord character, PrestigeBand band, WorldCalibration calibration)
    {
        int overall = WorldEconomy.Overall(character.Attrs, character.PrimaryPosition, calibration);
        int marketValue = WorldEconomy.MarketValueEur(overall, character.Age, band, character.PotentialGap, calibration);

        return character with
        {
            ShirtName = character.LastName.ToUpperInvariant(),
            Overall = overall,
            PotentialOverall = WorldEconomy.PotentialOverall(overall, character.PotentialGap),
            MarketValueEur = marketValue,
            SalaryMonthlyBrl = WorldEconomy.SalaryMonthlyBrl(marketValue, calibration),
        };
    }

    /// <summary>ΔE is stored to 1 decimal and luminance to 4, matching the precision the source
    /// batch recorded — rounding here is what keeps a re-imported club byte-identical.</summary>
    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
