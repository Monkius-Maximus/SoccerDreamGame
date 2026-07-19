namespace SoccerSim.Core.Ai.Utility;

/// <summary>Shape of a consideration's response curve.</summary>
public enum ResponseCurveKind
{
    /// <summary>y = x.</summary>
    Linear,

    /// <summary>y = x²; punishes weak inputs, rewards strong ones.</summary>
    Quadratic,

    /// <summary>y = √x; rewards even weak inputs quickly.</summary>
    SquareRoot,

    /// <summary>y = 1 − x; inverts the input.</summary>
    Inverse,
}

/// <summary>Maps a normalised input to a normalised score. Inputs are clamped to [0, 1].</summary>
public readonly record struct ResponseCurve(ResponseCurveKind Kind)
{
    public static ResponseCurve Linear => new(ResponseCurveKind.Linear);

    public static ResponseCurve Quadratic => new(ResponseCurveKind.Quadratic);

    public static ResponseCurve SquareRoot => new(ResponseCurveKind.SquareRoot);

    public static ResponseCurve Inverse => new(ResponseCurveKind.Inverse);

    public double Evaluate(double input)
    {
        double x = Math.Clamp(input, 0.0, 1.0);
        return Kind switch
        {
            ResponseCurveKind.Linear => x,
            ResponseCurveKind.Quadratic => x * x,
            ResponseCurveKind.SquareRoot => Math.Sqrt(x),
            ResponseCurveKind.Inverse => 1.0 - x,
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown response curve."),
        };
    }
}
