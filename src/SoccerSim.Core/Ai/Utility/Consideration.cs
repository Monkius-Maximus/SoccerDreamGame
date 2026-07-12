namespace SoccerSim.Core.Ai.Utility;

/// <summary>
/// One normalised input to an action score: a raw signal in [0, 1] (clamped at evaluation),
/// shaped by a response curve and weighted against its siblings.
/// </summary>
public readonly record struct Consideration
{
    public Consideration(string name, double input, ResponseCurve curve, double weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (weight <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "Consideration weights must be > 0.");

        Name = name;
        Input = input;
        Curve = curve;
        Weight = weight;
    }

    public string Name { get; }

    public double Input { get; }

    public ResponseCurve Curve { get; }

    public double Weight { get; }

    public double Score => Weight * Curve.Evaluate(Input);
}
