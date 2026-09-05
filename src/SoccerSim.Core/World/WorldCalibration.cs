namespace SoccerSim.Core.World;

/// <summary>One calibrated constant. <see cref="Note"/> is mandatory by policy, not convention —
/// DATA_CONTRACT.md §6: "Todo número aqui tem origem declarada" (the "no number without a
/// source" rule applies to the calibration table too, not just to club/player facts).</summary>
public sealed record CalibrationConstant(double Value, string Unit, string Note);

/// <summary>One rung of the age-multiplier step function: applies from <see cref="Age"/>
/// (inclusive) up to the next rung. See <see cref="WorldCalibration.AgeMultiplier"/>.</summary>
public sealed record AgeMultStep(int Age, double Multiplier);

public sealed record PrestigeBandCalibration(double ValueMult, double CapMean, double CapSd, int N);

public sealed record StadiumProfileEntry(double Mean, double Sd, double Min, double Max);

/// <summary>
/// Every number the economy and validation formulas depend on, as data — never a literal in
/// code (ROADMAP.md D-04). <see cref="AgeMult"/> must be supplied sorted ascending by
/// <see cref="AgeMultStep.Age"/>; the constructor does not re-sort it, since callers (JSON
/// import, the future calibration editor) own that order already.
/// </summary>
public sealed record WorldCalibration(
    IReadOnlyDictionary<string, CalibrationConstant> Constants,
    IReadOnlyList<AgeMultStep> AgeMult,
    IReadOnlyDictionary<PrestigeBand, PrestigeBandCalibration> Bands,
    IReadOnlyDictionary<AtmosphereArchetype, double> HomeAdv,
    IReadOnlyDictionary<string, StadiumProfileEntry> StadiumProfile,
    IReadOnlyDictionary<Position, IReadOnlyDictionary<Attr, double>> PositionWeights)
{
    public double Constant(string key) => Constants[key].Value;

    /// <summary>The multiplier for the last rung whose <c>Age</c> is &lt;= <paramref name="age"/>,
    /// per ALGORITHMS.md §2.2 ("escada, vale o último degrau com age &gt;= chave").</summary>
    public double AgeMultiplier(int age)
    {
        double multiplier = AgeMult[0].Multiplier;
        foreach (var step in AgeMult)
        {
            if (age >= step.Age)
                multiplier = step.Multiplier;
        }
        return multiplier;
    }
}
