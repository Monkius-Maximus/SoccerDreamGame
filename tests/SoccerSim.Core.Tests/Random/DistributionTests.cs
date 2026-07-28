using SoccerSim.Core.Random;
using Xunit;

namespace SoccerSim.Core.Tests.Random;

/// <summary>
/// As distribuições de <see cref="RandomStream"/>: ausência de viés, limites da gaussiana
/// truncada, pré-condições que lançam e o round-trip de estado que save/load exige.
/// </summary>
public sealed class DistributionTests
{
    private const ulong Master = 0xD1CED00D2026UL;

    private static RandomStream NewStream() => RandomStream.Create(Master, StreamName.WorldGeneration);

    private static RandomStream NewStream(StreamName name, params long[] ids)
        => RandomStream.Create(Master, name, ids);

    // ---------------------------------------------------------------- NextDouble

    [Fact]
    public void NextDouble_StaysInHalfOpenUnitInterval()
    {
        RandomStream stream = NewStream();

        for (int i = 0; i < 100_000; i++)
        {
            double value = stream.NextDouble();
            Assert.True(value >= 0.0 && value < 1.0, $"NextDouble() devolveu {value}, fora de [0, 1).");
        }
    }

    /// <summary>
    /// A escala é 2^-53 sobre os 53 bits altos: uma multiplicação exata em IEEE 754. Os extremos
    /// do draw crú mapeiam exatamente em 0 e em 1 − 2^-53.
    /// </summary>
    [Fact]
    public void NextDouble_UsesFiftyThreeBitsOfMantissa()
    {
        Assert.Equal(0.0, (0UL >> 11) * (1.0 / 9007199254740992.0));
        Assert.Equal(1.0 - Math.ScaleB(1.0, -53), (ulong.MaxValue >> 11) * (1.0 / 9007199254740992.0));
    }

    // ---------------------------------------------------------------- NextInt

    /// <summary>
    /// Critério de aceitação 4. <c>NextInt(0, 3)</c> é o pior caso do viés modular: 3 não divide
    /// 2^64, então reduzir a saída crua por <c>%</c> favoreceria sistematicamente os buckets
    /// baixos. A rejeição por máscara elimina isso.
    /// </summary>
    [Fact]
    public void NextInt_IsUnbiased_OverThreeMillionSamples()
    {
        const int samples = 3_000_000;
        const int buckets = 3;

        RandomStream stream = NewStream();
        int[] counts = new int[buckets];

        for (int i = 0; i < samples; i++)
        {
            int value = stream.NextInt(0, buckets);
            Assert.InRange(value, 0, buckets - 1);
            counts[value]++;
        }

        double expected = (double)samples / buckets;
        double chiSquare = 0.0;

        for (int i = 0; i < buckets; i++)
        {
            double deviationPercent = Math.Abs(counts[i] - expected) / expected * 100.0;
            Assert.True(deviationPercent < 0.5,
                $"Bucket {i} desviou {deviationPercent:F4}% do esperado ({counts[i]} vs {expected:F0}); limite é 0,5%.");

            chiSquare += Math.Pow(counts[i] - expected, 2) / expected;
        }

        // Valor crítico do qui-quadrado a 99% com 2 graus de liberdade.
        const double criticalValue99 = 9.210;
        Assert.True(chiSquare < criticalValue99,
            $"Qui-quadrado {chiSquare:F4} >= {criticalValue99} — a distribuição não passa a 99%.");
    }

    [Fact]
    public void NextInt_RespectsNegativeBounds()
    {
        RandomStream stream = NewStream();

        for (int i = 0; i < 10_000; i++)
            Assert.InRange(stream.NextInt(-5, 5), -5, 4);
    }

    [Fact]
    public void NextInt_FullIntRange_DoesNotOverflow()
    {
        RandomStream stream = NewStream();

        for (int i = 0; i < 1_000; i++)
            Assert.InRange(stream.NextInt(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
    }

    [Fact]
    public void NextInt_SingleValueRange_AlwaysReturnsIt()
    {
        RandomStream stream = NewStream();

        for (int i = 0; i < 100; i++)
            Assert.Equal(3, stream.NextInt(3, 4));
    }

    /// <summary>Fail-fast: intervalo vazio ou invertido não tem resposta correta, então lança.</summary>
    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 4)]
    [InlineData(0, int.MinValue)]
    public void NextInt_NonPositiveRange_Throws(int min, int max)
    {
        RandomStream stream = NewStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => { stream.NextInt(min, max); });
    }

    [Fact]
    public void Next_NonPositiveMax_Throws()
    {
        RandomStream stream = NewStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => { stream.Next(0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { stream.Next(-1); });
    }

    // ---------------------------------------------------------------- NextBool

    [Fact]
    public void NextBool_AtTheExtremes_IsDecided()
    {
        RandomStream stream = NewStream();

        for (int i = 0; i < 1_000; i++)
        {
            Assert.False(stream.NextBool(0.0));
            Assert.True(stream.NextBool(1.0));
        }
    }

    [Fact]
    public void NextBool_ApproximatesTheRequestedProbability()
    {
        const int samples = 200_000;
        RandomStream stream = NewStream();

        int hits = 0;
        for (int i = 0; i < samples; i++)
        {
            if (stream.NextBool(0.25))
                hits++;
        }

        Assert.InRange(hits / (double)samples, 0.245, 0.255);
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(1.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NextBool_ProbabilityOutsideUnitInterval_Throws(double probability)
    {
        RandomStream stream = NewStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => { stream.NextBool(probability); });
    }

    // ---------------------------------------------------------------- NextGaussianBounded

    /// <summary>Critério de aceitação 5.</summary>
    [Fact]
    public void NextGaussianBounded_MatchesMeanDeviationAndBounds()
    {
        const int samples = 1_000_000;
        const double mean = 50.0;
        const double stdDev = 15.0;
        const double bound = 6.0 * stdDev; // Irwin–Hall n = 12 trunca em ±6σ, por design.

        RandomStream stream = NewStream(StreamName.PlayerGeneration);

        double sum = 0.0;
        double sumOfSquares = 0.0;

        for (int i = 0; i < samples; i++)
        {
            double value = stream.NextGaussianBounded(mean, stdDev);

            Assert.True(value >= mean - bound && value <= mean + bound,
                $"Amostra {value} caiu fora de [{mean - bound}, {mean + bound}] — a cauda além de 6σ não deveria existir.");

            sum += value;
            sumOfSquares += value * value;
        }

        double observedMean = sum / samples;
        double observedStdDev = Math.Sqrt((sumOfSquares / samples) - (observedMean * observedMean));

        Assert.InRange(observedMean, mean - 0.05, mean + 0.05);
        Assert.InRange(observedStdDev, stdDev - 0.05, stdDev + 0.05);
    }

    [Fact]
    public void NextGaussianBounded_ZeroDeviation_IsTheMean()
    {
        RandomStream stream = NewStream();

        for (int i = 0; i < 100; i++)
            Assert.Equal(50.0, stream.NextGaussianBounded(50.0, 0.0));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void NextGaussianBounded_InvalidDeviation_Throws(double stdDev)
    {
        RandomStream stream = NewStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => { stream.NextGaussianBounded(50.0, stdDev); });
    }

    [Fact]
    public void NextGaussianBounded_NaNMean_Throws()
    {
        RandomStream stream = NewStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => { stream.NextGaussianBounded(double.NaN, 15.0); });
    }

    // ---------------------------------------------------------------- Shuffle

    [Fact]
    public void Shuffle_IsAPermutation()
    {
        List<int> items = Enumerable.Range(0, 200).ToList();

        NewStream().Shuffle(items);

        Assert.Equal(Enumerable.Range(0, 200), items.OrderBy(x => x));
    }

    [Fact]
    public void Shuffle_ActuallyReorders()
    {
        List<int> items = Enumerable.Range(0, 200).ToList();

        NewStream().Shuffle(items);

        Assert.NotEqual(Enumerable.Range(0, 200), items);
    }

    [Fact]
    public void Shuffle_IsDeterministic()
    {
        List<int> first = Enumerable.Range(0, 50).ToList();
        List<int> second = Enumerable.Range(0, 50).ToList();

        NewStream(StreamName.ClubIdentity, 4).Shuffle(first);
        NewStream(StreamName.ClubIdentity, 4).Shuffle(second);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Shuffle_DegenerateSizes_AreNoOps(int count)
    {
        List<int> items = Enumerable.Range(0, count).ToList();

        NewStream().Shuffle(items);

        Assert.Equal(Enumerable.Range(0, count), items);
    }

    [Fact]
    public void Shuffle_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => NewStream().Shuffle<int>(null!));

    // ---------------------------------------------------------------- NextGuid

    [Fact]
    public void NextGuid_IsDeterministicAndDistinct()
    {
        Guid[] first = Draw(NewStream(StreamName.LifeEvents), 100);
        Guid[] second = Draw(NewStream(StreamName.LifeEvents), 100);

        Assert.Equal(first, second);
        Assert.Equal(100, first.Distinct().Count());

        static Guid[] Draw(RandomStream stream, int count)
        {
            Guid[] guids = new Guid[count];
            for (int i = 0; i < count; i++)
                guids[i] = stream.NextGuid();
            return guids;
        }
    }

    // ---------------------------------------------------------------- estado

    /// <summary>Critério de aceitação 7: o round-trip que save/load precisa.</summary>
    [Fact]
    public void State_RoundTrips_ResumingExactlyWhereItStopped()
    {
        RandomStream stream = NewStream(StreamName.MatchSimulation, 99);

        for (int i = 0; i < 100; i++)
            stream.NextUInt64();

        ulong saved = stream.State;

        ulong[] afterSave = new ulong[100];
        for (int i = 0; i < afterSave.Length; i++)
            afterSave[i] = stream.NextUInt64();

        stream.RestoreState(saved);

        ulong[] afterRestore = new ulong[100];
        for (int i = 0; i < afterRestore.Length; i++)
            afterRestore[i] = stream.NextUInt64();

        Assert.Equal(afterSave, afterRestore);
    }

    [Fact]
    public void FromState_ResumesAnotherStream()
    {
        RandomStream original = NewStream(StreamName.BackgroundSimulation, 2026, 12);

        for (int i = 0; i < 37; i++)
            original.NextUInt64();

        RandomStream resumed = RandomStream.FromState(original.State);

        for (int i = 0; i < 100; i++)
            Assert.Equal(original.NextUInt64(), resumed.NextUInt64());
    }

    /// <summary>
    /// O estado é um único ulong — é isso que permite guardá-lo como <c>INTEGER</c> no schema
    /// SQLite existente, sem serialização binária customizada. SQLite guarda inteiros com sinal,
    /// então o valor viaja pelo padrão de bits em complemento de dois; a ida e a volta preservam
    /// o estado inclusive com o bit alto ligado.
    /// </summary>
    [Fact]
    public void State_SurvivesTheSignedIntegerRoundTripSqliteUses()
    {
        RandomStream stream = NewStream(StreamName.WorldGeneration, 5);

        for (int i = 0; i < 10; i++)
        {
            stream.NextUInt64();

            long asSigned = unchecked((long)stream.State);
            ulong recovered = unchecked((ulong)asSigned);

            Assert.Equal(stream.State, recovered);
        }
    }

    [Fact]
    public void Create_SameArguments_ProducesTheSameSequence()
    {
        RandomStream a = RandomStream.Create(Master, StreamName.ClubIdentity, 42);
        RandomStream b = RandomStream.Create(Master, StreamName.ClubIdentity, 42);

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
            Assert.Equal(a.NextDouble(), b.NextDouble());
            Assert.Equal(a.NextInt(-1000, 1000), b.NextInt(-1000, 1000));
            Assert.Equal(a.NextGaussianBounded(50, 15), b.NextGaussianBounded(50, 15));
            Assert.Equal(a.NextGuid(), b.NextGuid());
        }
    }

    [Fact]
    public void Create_SeedsFromTheFrozenDerivation()
        => Assert.Equal(
            SeedDerivation.Derive(Master, StreamName.ClubIdentity, 42),
            RandomStream.Create(Master, StreamName.ClubIdentity, 42).State);
}
