using SoccerSim.Core.MatchEngine;
using SoccerSim.Core.Random;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class PoissonSamplerTests
{
    [Fact]
    public void SameSeed_ProducesSameSamples()
    {
        var a = new SplitMix64Random(1);
        var b = new SplitMix64Random(1);

        for (int i = 0; i < 100; i++)
            Assert.Equal(PoissonSampler.Sample(a, 2.5), PoissonSampler.Sample(b, 2.5));
    }

    [Fact]
    public void EmpiricalMean_ApproximatesLambda()
    {
        var rng = new SplitMix64Random(99);
        const double lambda = 2.3;
        const int n = 20000;

        long sum = 0;
        for (int i = 0; i < n; i++)
            sum += PoissonSampler.Sample(rng, lambda);

        double mean = sum / (double)n;
        Assert.InRange(mean, lambda - 0.15, lambda + 0.15); // ~14 sigma band, robustly safe
    }

    [Fact]
    public void ZeroLambda_AlwaysZero()
    {
        var rng = new SplitMix64Random(7);
        for (int i = 0; i < 50; i++)
            Assert.Equal(0, PoissonSampler.Sample(rng, 0.0));
    }

    [Fact]
    public void NegativeLambda_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PoissonSampler.Sample(new SplitMix64Random(1), -1.0));
    }
}
