using SoccerSim.Core.Numerics;
using SoccerSim.Core.Pitch;
using Xunit;

namespace SoccerSim.Core.Tests.Numerics;

/// <summary>
/// O contrato de <see cref="DeterministicMath"/>. Duas famílias de teste, com papéis diferentes:
///
/// <list type="bullet">
///   <item><description>
///   <b>Vetores congelados</b> — os padrões de bits exatos das saídas. É a rede de segurança
///   contra a única premissa fraca desta classe (nenhuma contração em FMA, nenhum intermediário
///   com precisão estendida): numa plataforma que a quebrasse, isto falha alto em vez de gravar
///   saves silenciosamente incompatíveis.
///   </description></item>
///   <item><description>
///   <b>Precisão contra a biblioteca padrão</b> — quantifica o quanto o comportamento mudou em
///   relação ao que havia antes. Chamar a BCL aqui é legítimo: a política de determinismo varre
///   <c>src/</c>, e este é o lugar certo para usá-la como referência.
///   </description></item>
/// </list>
/// </summary>
public sealed class DeterministicMathTests
{
    private static ulong Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value));

    // ---------------------------------------------------------------- vetores congelados

    [Theory]
    [InlineData(0.0, 0x3FF0000000000000UL)]
    [InlineData(0.25, 0x3FFC73D51C54470EUL)]
    [InlineData(1.0, 0x4023FFFFFFFFFFFFUL)]
    [InlineData(-1.0, 0x3FB999999999999AUL)]
    [InlineData(2.5, 0x4073C3A4EDFA9759UL)]
    [InlineData(-2.5, 0x3F69E7C6E43390B6UL)]
    [InlineData(0.075, 0x3FF3041AE9610AD8UL)]
    public void Pow10_MatchesFrozenBitPattern(double x, ulong expected)
        => Assert.Equal(expected, Bits(DeterministicMath.Pow10(x)));

    [Theory]
    [InlineData(0.0, 0x0000000000000000UL, 0x3FF0000000000000UL)]
    [InlineData(0.1, 0x3FB98EAECB8BCB2CUL, 0x3FEFD712F9A817C0UL)]
    [InlineData(-0.1, 0xBFB98EAECB8BCB2CUL, 0x3FEFD712F9A817C0UL)]
    [InlineData(0.12, 0x3FBEA5758F3CE5CDUL, 0x3FEFC5169DC5B825UL)]
    [InlineData(1.0, 0x3FEAED548F090CEEUL, 0x3FE14A280FB5068CUL)]
    [InlineData(-3.0, 0xBFC210386DB6D55BUL, 0xBFEFAE04BE85E5D2UL)]
    public void SinCos_MatchFrozenBitPatterns(double radians, ulong expectedSin, ulong expectedCos)
    {
        Assert.Equal(expectedSin, Bits(DeterministicMath.Sin(radians)));
        Assert.Equal(expectedCos, Bits(DeterministicMath.Cos(radians)));
    }

    // ---------------------------------------------------------------- precisão

    /// <summary>
    /// O domínio que importa: <c>10^(Δelo/400)</c> para qualquer diferença de Elo concebível.
    /// </summary>
    [Fact]
    public void Pow10_TracksTheStandardLibrary_OverTheEloDomain()
    {
        double worst = 0.0;
        double worstAt = 0.0;

        for (int delta = -2000; delta <= 2000; delta++)
        {
            double x = delta / 400.0;
            double reference = Math.Pow(10.0, x);
            double relative = Math.Abs(DeterministicMath.Pow10(x) - reference) / reference;

            if (relative > worst)
            {
                worst = relative;
                worstAt = x;
            }
        }

        Assert.True(worst < 1e-13, $"Erro relativo máximo {worst:E3} em x = {worstAt}.");
    }

    /// <summary>
    /// O que realmente importa não é o ULP solto, e sim quanto a expectativa de vitória do modelo
    /// se desloca — porque é isso que muda placares gravados.
    /// </summary>
    [Fact]
    public void EloWinExpectation_IsIndistinguishableFromTheOldFormula()
    {
        double worst = 0.0;

        for (int gap = -800; gap <= 800; gap++)
        {
            double old = 1.0 / (1.0 + Math.Pow(10.0, gap / 400.0));
            double now = 1.0 / (1.0 + DeterministicMath.Pow10(gap / 400.0));
            worst = Math.Max(worst, Math.Abs(old - now));
        }

        Assert.True(worst < 1e-12, $"Deslocamento máximo na expectativa de vitória: {worst:E3}.");
    }

    [Fact]
    public void SinCos_TrackTheStandardLibrary_AcrossTheCircle()
    {
        double worstSin = 0.0;
        double worstCos = 0.0;

        for (int i = -3200; i <= 3200; i++)
        {
            double x = i / 1000.0; // ≈ [-π, π] com folga
            worstSin = Math.Max(worstSin, Math.Abs(DeterministicMath.Sin(x) - Math.Sin(x)));
            worstCos = Math.Max(worstCos, Math.Abs(DeterministicMath.Cos(x) - Math.Cos(x)));
        }

        Assert.True(worstSin < 1e-15, $"Erro absoluto máximo em Sin: {worstSin:E3}.");
        Assert.True(worstCos < 1e-15, $"Erro absoluto máximo em Cos: {worstCos:E3}.");
    }

    /// <summary>O domínio real de uso: o erro de ângulo de passe/chute nunca passa de ~0,12 rad.</summary>
    [Fact]
    public void SinCos_AreEssentiallyExact_OverThePassAndShotDomain()
    {
        for (int i = -1200; i <= 1200; i++)
        {
            double x = i / 10000.0;

            // 5e-16 é ~2 ULP de um valor da ordem de 1; a medição dá 1 ULP.
            Assert.True(Math.Abs(DeterministicMath.Sin(x) - Math.Sin(x)) < 5e-16);
            Assert.True(Math.Abs(DeterministicMath.Cos(x) - Math.Cos(x)) < 5e-16);
        }
    }

    [Fact]
    public void SinCos_SatisfyThePythagoreanIdentity()
    {
        for (int i = -6280; i <= 6280; i += 7)
        {
            double x = i / 1000.0;
            double sin = DeterministicMath.Sin(x);
            double cos = DeterministicMath.Cos(x);

            Assert.True(Math.Abs((sin * sin) + (cos * cos) - 1.0) < 1e-15, $"Falhou em {x}.");
        }
    }

    // ---------------------------------------------------------------- casos exatos e simetrias

    [Fact]
    public void Pow10_OfZero_IsExactlyOne() => Assert.Equal(1.0, DeterministicMath.Pow10(0.0));

    [Fact]
    public void SinCos_OfZero_AreExact()
    {
        Assert.Equal(0.0, DeterministicMath.Sin(0.0));
        Assert.Equal(1.0, DeterministicMath.Cos(0.0));
    }

    [Fact]
    public void Sin_IsOdd_AndCos_IsEven()
    {
        for (int i = 1; i <= 500; i++)
        {
            double x = i / 100.0;

            Assert.True(Math.Abs(DeterministicMath.Sin(-x) + DeterministicMath.Sin(x)) < 1e-16);
            Assert.True(Math.Abs(DeterministicMath.Cos(-x) - DeterministicMath.Cos(x)) < 1e-16);
        }
    }

    [Fact]
    public void Pow10_IsStrictlyIncreasing()
    {
        double previous = DeterministicMath.Pow10(-5.0);

        for (int i = -4999; i <= 5000; i++)
        {
            double current = DeterministicMath.Pow10(i / 1000.0);
            Assert.True(current > previous, $"Não é monotônica em {i / 1000.0}.");
            previous = current;
        }
    }

    [Fact]
    public void Results_AreReproducibleWithinTheProcess()
    {
        for (int i = -100; i <= 100; i++)
        {
            double x = i / 37.0;
            Assert.Equal(Bits(DeterministicMath.Pow10(x / 100.0)), Bits(DeterministicMath.Pow10(x / 100.0)));
            Assert.Equal(Bits(DeterministicMath.Sin(x)), Bits(DeterministicMath.Sin(x)));
            Assert.Equal(Bits(DeterministicMath.Cos(x)), Bits(DeterministicMath.Cos(x)));
        }
    }

    // ---------------------------------------------------------------- fail-fast

    [Theory]
    [InlineData(300.0001)]
    [InlineData(-300.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Pow10_OutsideItsDomain_Throws(double x)
        => Assert.Throws<ArgumentOutOfRangeException>(() => { DeterministicMath.Pow10(x); });

    [Theory]
    [InlineData(1.0000001e6)]
    [InlineData(-1.0000001e6)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void SinCos_OutsideTheirDomain_Throw(double radians)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { DeterministicMath.Sin(radians); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { DeterministicMath.Cos(radians); });
    }

    // ---------------------------------------------------------------- o consumidor

    /// <summary>
    /// <see cref="Vec2.Rotated"/> é o call site que motivou a trigonometria própria. Rotação
    /// preserva comprimento e compõe — se a troca tivesse quebrado algo, quebra aqui.
    /// </summary>
    [Fact]
    public void Vec2Rotated_PreservesLengthAndComposes()
    {
        var v = new Vec2(3.0, -4.0);

        Assert.Equal(5.0, v.Length, 12);

        for (int i = -120; i <= 120; i++)
        {
            double angle = i / 1000.0;
            Assert.Equal(5.0, v.Rotated(angle).Length, 12);
        }

        Vec2 twoSteps = v.Rotated(0.05).Rotated(0.07);
        Vec2 oneStep = v.Rotated(0.12);

        Assert.Equal(oneStep.X, twoSteps.X, 12);
        Assert.Equal(oneStep.Y, twoSteps.Y, 12);
    }

    [Fact]
    public void Vec2Rotated_ByZero_IsTheIdentity()
    {
        var v = new Vec2(1.25, -2.5);
        Vec2 rotated = v.Rotated(0.0);

        Assert.Equal(v.X, rotated.X);
        Assert.Equal(v.Y, rotated.Y);
    }
}
