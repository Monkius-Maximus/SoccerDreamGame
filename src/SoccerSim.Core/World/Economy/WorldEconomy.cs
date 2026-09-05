namespace SoccerSim.Core.World.Economy;

/// <summary>
/// The economy formulas that turn attributes into overall, market value and salary
/// (ALGORITHMS.md §2). Every constant is read from <see cref="WorldCalibration"/> — never a
/// literal here — so re-fitting the curve (it already moved three times per DATA_CONTRACT.md §6)
/// never requires a code change. Verified bit-for-bit against all 688 players and 20 clubs of
/// the source dataset (design_handoff_ferramenta_de_mundo/data/world.json): this is the Sprint 1
/// hard gate (ROADMAP.md) — <see cref="SoccerSim.Core.Tests.World.EconomyTests"/>.
/// </summary>
public static class WorldEconomy
{
    /// <summary>overall = round(Σ positionWeights[position][attr] × attrs[attr]).</summary>
    public static int Overall(IReadOnlyDictionary<Attr, int> attrs, Position position, WorldCalibration calibration)
    {
        double sum = 0;
        foreach (var (attr, weight) in calibration.PositionWeights[position])
            sum += weight * attrs[attr];
        return RoundAwayFromZero(sum);
    }

    /// <summary>potentialOverall = max(overall, overall + potentialGap).</summary>
    public static int PotentialOverall(int overall, int potentialGap) => Math.Max(overall, overall + potentialGap);

    /// <summary>
    /// Market value: an exponential curve on overall (doubling every <c>valueDoublingStep</c>
    /// points above <c>valuePivot</c>), scaled by age, prestige band and potential premium, then
    /// rounded to the nearest 5,000 EUR and floored — ALGORITHMS.md §2.2.
    /// </summary>
    public static int MarketValueEur(int overall, int age, PrestigeBand band, int potentialGap, WorldCalibration calibration)
    {
        double valueBase = calibration.Constant("valueBase");
        double valuePivot = calibration.Constant("valuePivot");
        double doublingStep = calibration.Constant("valueDoublingStep");
        double potentialPremium = calibration.Constant("potentialPremium");
        double valueFloor = calibration.Constant("valueFloorEur");

        double ageMult = calibration.AgeMultiplier(age);
        double bandMult = calibration.Bands[band].ValueMult;

        double value = valueBase
            * Math.Pow(2, (overall - valuePivot) / doublingStep)
            * ageMult
            * bandMult
            * (1 + potentialPremium * potentialGap);

        double rounded = RoundAwayFromZero(value / 5000.0) * 5000.0;
        return (int)Math.Max(valueFloor, rounded);
    }

    /// <summary>Salary is derived from value alone (the band already shaped value) — rounded to
    /// the nearest 1,000 BRL and floored, ALGORITHMS.md §2.3.</summary>
    public static int SalaryMonthlyBrl(int marketValueEur, WorldCalibration calibration)
    {
        double eurToBrl = calibration.Constant("eurToBrl");
        double wageRate = calibration.Constant("wageRateMonthly");
        double wageFloor = calibration.Constant("wageFloorBrl");

        double salary = RoundAwayFromZero(marketValueEur * eurToBrl * wageRate / 1000.0) * 1000.0;
        return (int)Math.Max(wageFloor, salary);
    }

    /// <summary>Matches JS <c>Math.round</c> (half rounds toward +Infinity) for the always-positive
    /// domain values here; <see cref="MidpointRounding.AwayFromZero"/> is equivalent for x &gt;= 0.</summary>
    private static int RoundAwayFromZero(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);
}
