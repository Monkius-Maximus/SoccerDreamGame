using SoccerSim.Core.Random;
using Xunit;

namespace SoccerSim.Core.Tests.Random;

/// <summary>
/// Vetores de referência do SplitMix64 (Vigna, <c>prng.di.unimi.it/splitmix64.c</c>).
///
/// <para>
/// Estas constantes são o contrato do gerador com o resto do universo: se elas não baterem, a
/// implementação está errada — <b>o teste não se ajusta ao código</b>. Um único bit fora daqui
/// invalida todo save, todo replay e todo harness de calibração.
/// </para>
/// </summary>
public sealed class SplitMix64VectorTests
{
    /// <summary>As 8 primeiras saídas de cada seed de referência. CONGELADO.</summary>
    public static TheoryData<ulong, string[]> ReferenceVectors => new()
    {
        {
            0UL,
            new[]
            {
                "E220A8397B1DCDAF", "6E789E6AA1B965F4", "06C45D188009454F", "F88BB8A8724C81EC",
                "1B39896A51A8749B", "53CB9F0C747EA2EA", "2C829ABE1F4532E1", "C584133AC916AB3C",
            }
        },
        {
            1UL,
            new[]
            {
                "910A2DEC89025CC1", "BEEB8DA1658EEC67", "F893A2EEFB32555E", "71C18690EE42C90B",
                "71BB54D8D101B5B9", "C34D0BFF90150280", "E099EC6CD7363CA5", "85E7BB0F12278575",
            }
        },
        {
            42UL,
            new[]
            {
                "BDD732262FEB6E95", "28EFE333B266F103", "47526757130F9F52", "581CE1FF0E4AE394",
                "09BC585A244823F2", "DE4431FA3C80DB06", "37E9671C45376D5D", "CCF635EE9E9E2FA4",
            }
        },
        {
            0xDEADBEEFUL,
            new[]
            {
                "4ADFB90F68C9EB9B", "DE586A3141A10922", "021FBC2F8E1CFC1D", "7466CE737BE16790",
                "3BFA8764F685BD1C", "AB203E503CB55B3F", "5A2FDC2BF68CEDB3", "B30A4CCF430B1B5A",
            }
        },
    };

    [Theory]
    [MemberData(nameof(ReferenceVectors))]
    public void Next_MatchesReferenceVectors(ulong seed, string[] expected)
    {
        var generator = new SplitMix64(seed);

        string[] actual = new string[expected.Length];
        for (int i = 0; i < expected.Length; i++)
            actual[i] = generator.Next().ToString("X16");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SameSeed_ReproducesSequence()
    {
        var a = new SplitMix64(123456789UL);
        var b = new SplitMix64(123456789UL);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.Next(), b.Next());
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = new SplitMix64(1UL);
        var b = new SplitMix64(2UL);

        bool diverged = false;
        for (int i = 0; i < 10; i++)
            diverged |= a.Next() != b.Next();

        Assert.True(diverged);
    }

    /// <summary>
    /// A soma da gamma e as duas multiplicações do <c>Mix</c> transbordam o tempo todo. Semear com
    /// <see cref="ulong.MaxValue"/> força wraparound já no primeiro passo: se algum dia alguém
    /// ligar <c>CheckForOverflowUnderflow</c> e um bloco <c>unchecked</c> tiver se perdido, isto
    /// vira <see cref="OverflowException"/> em vez de um número.
    /// </summary>
    [Fact]
    public void Arithmetic_WrapsAroundInsteadOfThrowing()
    {
        var generator = new SplitMix64(ulong.MaxValue);

        for (int i = 0; i < 100; i++)
            generator.Next();

        // O estado deu a volta em 2^64: seed + 100 * gamma, com wraparound.
        Assert.Equal(unchecked(ulong.MaxValue + (100UL * SplitMix64.GoldenGamma)), generator.State);
    }

    /// <summary>
    /// <c>Mix</c> é a finalizadora pura — sem incremento de estado. Verificado contra o primeiro
    /// vetor de referência: com seed 0, o primeiro <c>Next()</c> é exatamente <c>Mix(gamma)</c>.
    /// </summary>
    [Fact]
    public void Mix_IsTheFinaliserWithoutTheStateIncrement()
    {
        Assert.Equal(0xE220A8397B1DCDAFUL, SplitMix64.Mix(SplitMix64.GoldenGamma));
        Assert.Equal(new SplitMix64(0UL).Next(), SplitMix64.Mix(SplitMix64.GoldenGamma));
    }

    [Fact]
    public void State_IsTheWholeGenerator()
    {
        var generator = new SplitMix64(7UL);
        Assert.Equal(7UL, generator.State);

        generator.Next();
        Assert.Equal(unchecked(7UL + SplitMix64.GoldenGamma), generator.State);
    }
}
