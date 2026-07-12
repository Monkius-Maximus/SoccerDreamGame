namespace SoccerSim.Core.Tactics;

/// <summary>Defensive ↔ attacking posture. Drives line height and risk appetite.</summary>
public enum Mentality
{
    VeryDefensive,
    Defensive,
    Balanced,
    Attacking,
    VeryAttacking,
}

public static class MentalityExtensions
{
    /// <summary>The mentality as a symmetric lean in [−1, +1] (0 = balanced).</summary>
    public static double Lean(this Mentality mentality) => mentality switch
    {
        Mentality.VeryDefensive => -1.0,
        Mentality.Defensive => -0.5,
        Mentality.Balanced => 0.0,
        Mentality.Attacking => 0.5,
        Mentality.VeryAttacking => 1.0,
        _ => throw new ArgumentOutOfRangeException(nameof(mentality), mentality, "Unknown mentality."),
    };
}

/// <summary>
/// The whole team tactic as data — formation anchor points, mentality, and instruction dials.
/// The tactic never contains behaviour; it only tilts weights (see <c>BehaviourWeights.Derive</c>),
/// and the on-pitch behaviour emerges from those weights.
/// </summary>
public sealed record TeamTactics
{
    public TeamTactics(Formation formation, Mentality mentality, TeamInstructions instructions)
    {
        ArgumentNullException.ThrowIfNull(formation);
        ArgumentNullException.ThrowIfNull(instructions);
        if (!Enum.IsDefined(mentality))
            throw new ArgumentOutOfRangeException(nameof(mentality), mentality, "Unknown mentality.");

        Formation = formation;
        Mentality = mentality;
        Instructions = instructions;
    }

    public Formation Formation { get; }

    public Mentality Mentality { get; }

    public TeamInstructions Instructions { get; }

    public static TeamTactics Default => new(Formation.FourFourTwo(), Mentality.Balanced, TeamInstructions.Balanced);
}
