using SoccerSim.Core.Events;

namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Deterministic Poisson sampler using Knuth's multiplicative method. Every draw comes from the
/// injected <see cref="IRandom"/> (in practice an <c>IDeterministicRandom</c>), never
/// <see cref="System.Random"/>, so a seed reproduces a scoreline exactly.
/// </summary>
public static class PoissonSampler
{
    // Defensive bound: for very large lambda, e^-lambda underflows to 0 and the product never
    // drops below it. Real football lambdas are ~0..4, so this is never reached in practice.
    private const int MaxIterations = 10_000;

    /// <summary>
    /// Sample <c>k ~ Poisson(lambda)</c> by multiplying uniforms until the running product falls
    /// below <c>e^-lambda</c>. Throws on a negative lambda (fail-fast).
    /// </summary>
    public static int Sample(IRandom rng, double lambda)
    {
        if (rng is null)
            throw new ArgumentNullException(nameof(rng));
        if (lambda < 0.0)
            throw new ArgumentOutOfRangeException(nameof(lambda), $"lambda must be >= 0, was {lambda}.");

        double threshold = Math.Exp(-lambda);
        int k = 0;
        double product = 1.0;
        do
        {
            k++;
            product *= rng.NextDouble();
        }
        while (product > threshold && k < MaxIterations);

        return k - 1;
    }
}
