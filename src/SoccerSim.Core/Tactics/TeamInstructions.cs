namespace SoccerSim.Core.Tactics;

/// <summary>
/// Team-level instruction dials, each a normalised [0, 1] scale (0.5 = neutral). These are
/// pure parameters: they only take effect through the single weight-derivation point
/// (<c>BehaviourWeights.Derive</c>). Out-of-range values throw.
/// </summary>
public sealed record TeamInstructions
{
    public TeamInstructions(double pressingIntensity, double passingDirectness, double width, double tempo)
    {
        PressingIntensity = ValidateUnit(pressingIntensity, nameof(pressingIntensity));
        PassingDirectness = ValidateUnit(passingDirectness, nameof(passingDirectness));
        Width = ValidateUnit(width, nameof(width));
        Tempo = ValidateUnit(tempo, nameof(tempo));
    }

    /// <summary>How far from goal and how hard the team hunts the ball carrier.</summary>
    public double PressingIntensity { get; }

    /// <summary>0 = patient short passing, 1 = direct long balls forward.</summary>
    public double PassingDirectness { get; }

    /// <summary>How stretched the formation sits across the pitch width.</summary>
    public double Width { get; }

    /// <summary>How eagerly the team plays forward and carries into space.</summary>
    public double Tempo { get; }

    public static TeamInstructions Balanced => new(0.5, 0.5, 0.5, 0.5);

    private static double ValidateUnit(double value, string name)
        => value is >= 0.0 and <= 1.0
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Team instructions are normalised scales and must be within [0, 1].");
}
