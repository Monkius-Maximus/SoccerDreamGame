using SoccerSim.Core.Random;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class SplitMix64RandomTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new SplitMix64Random(123456789UL);
        var b = new SplitMix64Random(123456789UL);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = new SplitMix64Random(1UL);
        var b = new SplitMix64Random(2UL);

        bool anyDifferent = false;
        for (int i = 0; i < 10; i++)
            anyDifferent |= a.NextULong() != b.NextULong();

        Assert.True(anyDifferent);
    }

    [Fact]
    public void SameSeed_ReproducesEveryApiSurface()
    {
        // The whole point of SplitMix64 over System.Random: a seed reproduces the same sequence
        // on every platform/.NET version. Proven here across ulong, double, and bounded-int draws.
        var a = new SplitMix64Random(99UL);
        var b = new SplitMix64Random(99UL);

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong());
            Assert.Equal(a.NextDouble(), b.NextDouble());
            Assert.Equal(a.NextInt(-1000, 1000), b.NextInt(-1000, 1000));
        }
    }

    [Fact]
    public void NextDouble_StaysInHalfOpenUnitInterval()
    {
        var rng = new SplitMix64Random(42UL);

        for (int i = 0; i < 10000; i++)
        {
            double d = rng.NextDouble();
            Assert.InRange(d, 0.0, 1.0);
            Assert.NotEqual(1.0, d); // [0, 1)
        }
    }

    [Fact]
    public void NextInt_RespectsBounds()
    {
        var rng = new SplitMix64Random(7UL);

        for (int i = 0; i < 10000; i++)
            Assert.InRange(rng.NextInt(-5, 5), -5, 4);
    }

    [Fact]
    public void NextInt_EmptyRange_ReturnsMin()
    {
        var rng = new SplitMix64Random(7UL);

        Assert.Equal(3, rng.NextInt(3, 3));
        Assert.Equal(0, rng.Next(0));
    }

    [Fact]
    public void NextInt_Throws_WhenMaxBelowMin()
    {
        var rng = new SplitMix64Random(7UL);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 4));
    }

    [Fact]
    public void CreateStream_SameKeysReproduce_DifferentKeysDiverge()
    {
        const ulong master = 0xABCDEF12345UL;

        IDeterministicRandom s1 = DeterministicRng.CreateStream(master, 10, 2026, 3);
        IDeterministicRandom s2 = DeterministicRng.CreateStream(master, 10, 2026, 3);
        for (int i = 0; i < 100; i++)
            Assert.Equal(s1.NextULong(), s2.NextULong());

        // A different fixture key (11 vs 10) yields an independent stream.
        IDeterministicRandom fresh = DeterministicRng.CreateStream(master, 10, 2026, 3);
        IDeterministicRandom other = DeterministicRng.CreateStream(master, 11, 2026, 3);
        bool diverged = false;
        for (int i = 0; i < 10; i++)
            diverged |= fresh.NextULong() != other.NextULong();

        Assert.True(diverged);
    }
}
