namespace SoccerSim.Core.Random;

/// <summary>
/// Entry point for deterministic randomness. Creates root generators from a world's master
/// seed and — more importantly — derives ISOLATED child streams from a master seed plus a set
/// of stream keys, e.g. <c>hash(masterSeed, fixtureId, season, round)</c>. Two streams with
/// different keys are independent; the same keys always reproduce the same stream, which is
/// what lets a single match be re-simulated in isolation without disturbing the rest of the world.
/// </summary>
public static class DeterministicRng
{
    /// <summary>A root generator seeded directly from the world master seed.</summary>
    public static IDeterministicRandom Create(ulong masterSeed) => new SplitMix64Random(masterSeed);

    /// <summary>
    /// An isolated stream derived from <paramref name="masterSeed"/> and the ordered
    /// <paramref name="streamKeys"/>. Order matters: <c>(a, b)</c> and <c>(b, a)</c> yield
    /// different streams. Combining is a hash mix, so nearby keys still produce well-separated streams.
    /// </summary>
    public static IDeterministicRandom CreateStream(ulong masterSeed, params ulong[] streamKeys)
    {
        ulong seed = SplitMix64Random.Mix(masterSeed);
        foreach (ulong key in streamKeys)
            seed = SplitMix64Random.Mix(seed ^ SplitMix64Random.Mix(key));
        return new SplitMix64Random(seed);
    }
}
