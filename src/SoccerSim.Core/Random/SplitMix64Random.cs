namespace SoccerSim.Core.Random;

/// <summary>
/// SplitMix64 PRNG (Steele, Lea &amp; Flood, 2014) in pure C#. Tiny, fast, and — crucially —
/// bit-for-bit reproducible across platforms and .NET versions, because every step is plain
/// 64-bit integer arithmetic with fixed constants. This is the ONE random source the
/// simulation uses; there is deliberately no <see cref="System.Random"/> fallback.
/// </summary>
public sealed class SplitMix64Random : IDeterministicRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    /// <param name="seed">The stream seed. The same seed always yields the same sequence.</param>
    public SplitMix64Random(ulong seed) => _state = seed;

    public ulong NextULong()
    {
        unchecked
        {
            _state += GoldenGamma;
            return Mix(_state);
        }
    }

    /// <summary>A double in <c>[0, 1)</c>, built from the top 53 bits so every value is equally likely.</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive < minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                $"maxExclusive ({maxExclusive}) must be >= minInclusive ({minInclusive}).");

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        if (range == 0)
            return minInclusive; // degenerate empty range; mirrors System.Random.Next(min, min).

        // Unbiased bounded draw via rejection sampling: discard the short final bucket so every
        // value in the range is exactly equally likely (no modulo bias).
        ulong threshold = (0UL - range) % range; // == 2^64 mod range
        ulong x;
        do
        {
            x = NextULong();
        }
        while (x < threshold);

        return (int)((long)minInclusive + (long)(x % range));
    }

    public int Next(int maxExclusive) => NextInt(0, maxExclusive);

    /// <summary>The SplitMix64 finalising mix; also used to derive child stream seeds.</summary>
    internal static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
