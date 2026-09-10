using SoccerSim.Core.Events;

namespace SoccerSim.Core.Random;

/// <summary>
/// A fully deterministic, cross-platform pseudo-random source. Extends the minimal
/// <see cref="IRandom"/> seam used across the simulation with the wider integer surface
/// (raw 64-bit draws and bounded ints) that hierarchical, per-entity streams need.
///
/// <para>
/// Determinism is a hard requirement: the same seed MUST produce the same sequence on every
/// platform and every .NET version. That is exactly why simulation code never touches
/// <see cref="System.Random"/>, whose sequence is an unspecified implementation detail.
/// </para>
/// </summary>
public interface IDeterministicRandom : IRandom
{
    /// <summary>The next raw 64-bit draw.</summary>
    ulong NextULong();

    /// <summary>A uniformly distributed int in <c>[minInclusive, maxExclusive)</c>.</summary>
    int NextInt(int minInclusive, int maxExclusive);

    // The three draws below are DERIVED from the primitives above, not properties of any
    // particular generator, so they are implemented once here rather than repeated in every
    // implementation. Squad generation needs all three (ALGORITHMS.md §6): a normal draw for
    // attributes, heights and role targets, a uniform pick for names, and a weighted pick for
    // nationality and build type.

    /// <summary>A standard normal draw (mean 0, standard deviation 1), by Box-Muller.</summary>
    /// <remarks>
    /// Box-Muller produces two independent normals per pair of uniforms; this returns one and
    /// discards the other. Keeping the spare would save a draw but would put hidden state in the
    /// generator, making the sequence depend on how calls interleave with other draws. For a
    /// source whose entire purpose is reproducibility, the cheaper option is not worth the
    /// subtlety.
    /// </remarks>
    double NextGaussian()
    {
        // u1 must be non-zero: log(0) is negative infinity.
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>A uniformly chosen element.</summary>
    T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("Cannot pick from an empty list.", nameof(items));

        return items[NextInt(0, items.Count)];
    }

    /// <summary>
    /// An element chosen in proportion to its weight. Weights need not sum to anything in
    /// particular; they are relative.
    /// </summary>
    T Weighted<T>(IReadOnlyList<(T Item, double Weight)> options)
    {
        if (options.Count == 0)
            throw new ArgumentException("Cannot pick from an empty list.", nameof(options));

        double total = 0;
        foreach ((_, double weight) in options)
        {
            if (weight < 0)
                throw new ArgumentException("Weights must not be negative.", nameof(options));
            total += weight;
        }

        if (total <= 0)
            throw new ArgumentException("At least one weight must be positive.", nameof(options));

        double roll = NextDouble() * total;
        foreach ((T item, double weight) in options)
        {
            roll -= weight;
            if (roll < 0)
                return item;
        }

        // Only reachable through floating-point accumulation at the very top of the range.
        return options[^1].Item;
    }
}
