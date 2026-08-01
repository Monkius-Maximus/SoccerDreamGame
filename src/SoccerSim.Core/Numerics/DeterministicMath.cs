namespace SoccerSim.Core.Numerics;

/// <summary>
/// As funções transcendentais de que a simulação precisa, escritas com <b>apenas</b>
/// <c>+ − × ÷</c> e comparações — as operações para as quais a IEEE 754 garante arredondamento
/// correto, e portanto o mesmo resultado bit a bit em qualquer plataforma.
///
/// <para>
/// <b>Por que isto existe.</b> É o mesmo argumento que tirou Box–Muller da gaussiana
/// (<see cref="SoccerSim.Core.Random.RandomStream.NextGaussianBounded"/>): potência, seno e
/// cosseno da biblioteca padrão delegam à libm da plataforma, que <b>não</b> tem arredondamento
/// correto garantido. Um único bit de diferença entre duas máquinas — ou entre duas versões de
/// runtime — e a partida deixa de ser replayável. Essas chamadas estavam no caminho de simulação
/// em dois lugares: a expectativa logística de Elo dos resolvedores e o erro de ângulo de
/// passe/chute do <see cref="SoccerSim.Core.Pitch.Vec2.Rotated"/>.
/// </para>
///
/// <para>
/// <b>Precisão medida contra a libm</b> (não é o objetivo, mas define o quanto o comportamento
/// muda em relação ao que havia antes):
/// <list type="bullet">
///   <item><description><see cref="Sin"/>/<see cref="Cos"/>: ≤ 1 ULP em todo o domínio suportado.</description></item>
///   <item><description><see cref="Pow10"/>: ≤ 16 ULP para |x| ≤ 5 (que cobre qualquer diferença de Elo
///   concebível), degradando para ~650 ULP perto de |x| = 300. Na expectativa de vitória do Elo
///   isso dá uma diferença máxima de 1 ULP de probabilidade — irrelevante para o modelo,
///   e o que importa é que agora é a <i>mesma</i> diferença em toda máquina.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Ressalva honesta.</b> Estas funções são cadeias longas de aritmética em <c>double</c>, e não
/// operações inteiras: a reprodutibilidade depende de o runtime não fundir <c>a*b+c</c> em FMA nem
/// manter precisão estendida em intermediários. O .NET em x64/ARM64 não faz nenhuma das duas
/// coisas, mas isso é uma garantia mais fraca que a do gerador. Por isso os testes fixam os
/// padrões de bits exatos das saídas: numa plataforma que quebrasse a premissa, os vetores falham
/// alto em vez de corromper saves em silêncio.
/// </para>
///
/// <para>
/// Os coeficientes e as constantes de redução abaixo estão <b>congelados</b> pelo mesmo motivo que
/// <see cref="SoccerSim.Core.Random.SeedDerivation"/>: alterá-los muda resultados de partidas já
/// gravadas.
/// </para>
/// </summary>
public static class DeterministicMath
{
    /// <summary>Maior |x| aceito por <see cref="Pow10"/>. Além disso a redução perde sentido — e nada no jogo chega perto.</summary>
    private const double MaxPow10Exponent = 300.0;

    /// <summary>Maior |ângulo| aceito por <see cref="Sin"/>/<see cref="Cos"/>, em radianos.</summary>
    private const double MaxAngleRadians = 1.0e6;

    private const double Log2Of10 = 3.321928094887362;

    // 2^f para f ∈ [-0.5, 0.5]: série de Taylor de exp(f·ln2), coeficientes (ln2)^n / n!.
    // 13 termos deixam o erro de truncamento abaixo de 1 ULP em todo o intervalo.
    private const double E0 = 1.0;
    private const double E1 = 0.6931471805599453;
    private const double E2 = 0.2402265069591007;
    private const double E3 = 0.055504108664821576;
    private const double E4 = 0.009618129107628477;
    private const double E5 = 0.0013333558146428441;
    private const double E6 = 0.00015403530393381606;
    private const double E7 = 1.5252733804059838e-05;
    private const double E8 = 1.3215486790144305e-06;
    private const double E9 = 1.0178086009239696e-07;
    private const double E10 = 7.054911620801121e-09;
    private const double E11 = 4.44553827187081e-10;
    private const double E12 = 2.5678435993488196e-11;

    private const double TwoOverPi = 0.6366197723675814;

    // π/2 partido em três pedaços (Cody–Waite). Subtrair n·π/2 em três passos mantém a redução
    // exata muito além do que um único π/2 arredondado aguentaria — é o que segura 1 ULP.
    private const double HalfPiHigh = 1.5707963267341256;
    private const double HalfPiMid = 6.077100506303966e-11;
    private const double HalfPiLow = 2.0222662487959506e-21;

    // sin(r) = r + r³·P(r²) para |r| ≤ π/4; coeficientes (-1)^k / (2k+1)!, de r³ a r¹⁵.
    private const double S3 = -0.16666666666666666;
    private const double S5 = 0.008333333333333333;
    private const double S7 = -0.0001984126984126984;
    private const double S9 = 2.7557319223985893e-06;
    private const double S11 = -2.505210838544172e-08;
    private const double S13 = 1.6059043836821613e-10;
    private const double S15 = -7.647163731819816e-13;

    // cos(r) = 1 + r²·Q(r²) para |r| ≤ π/4; coeficientes (-1)^k / (2k)!, de r² a r¹⁶.
    private const double C2 = -0.5;
    private const double C4 = 0.041666666666666664;
    private const double C6 = -0.001388888888888889;
    private const double C8 = 2.48015873015873e-05;
    private const double C10 = -2.755731922398589e-07;
    private const double C12 = 2.08767569878681e-09;
    private const double C14 = -1.1470745597729725e-11;
    private const double C16 = 4.779477332387385e-14;

    /// <summary>
    /// <c>10^x</c>. Ocupa o lugar da exponenciação da biblioteca padrão na expectativa logística de Elo.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se |<paramref name="x"/>| &gt; 300, ou se é NaN.</exception>
    public static double Pow10(double x)
    {
        // Negado para que NaN, que falha toda comparação, também caia aqui.
        if (!(Math.Abs(x) <= MaxPow10Exponent))
        {
            throw new ArgumentOutOfRangeException(nameof(x), x,
                $"Pow10 supports |x| <= {MaxPow10Exponent}.");
        }

        return Exp2(x * Log2Of10);
    }

    /// <summary>
    /// <c>sin(radians)</c>. Ocupa o lugar do seno da biblioteca padrão em <see cref="SoccerSim.Core.Pitch.Vec2.Rotated"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se |<paramref name="radians"/>| &gt; 1e6, ou se é NaN.</exception>
    public static double Sin(double radians)
    {
        (int quadrant, double r) = Reduce(radians);

        return quadrant switch
        {
            0 => SinKernel(r),
            1 => CosKernel(r),
            2 => -SinKernel(r),
            _ => -CosKernel(r),
        };
    }

    /// <summary>
    /// <c>cos(radians)</c>. Ocupa o lugar do cosseno da biblioteca padrão em <see cref="SoccerSim.Core.Pitch.Vec2.Rotated"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se |<paramref name="radians"/>| &gt; 1e6, ou se é NaN.</exception>
    public static double Cos(double radians)
    {
        (int quadrant, double r) = Reduce(radians);

        return quadrant switch
        {
            0 => CosKernel(r),
            1 => -SinKernel(r),
            2 => -CosKernel(r),
            _ => SinKernel(r),
        };
    }

    /// <summary>
    /// <c>2^x</c>, separado em parte inteira e fracionária: a inteira vira escala exata por
    /// potência de dois (só mexe no expoente, erro zero) e só a fracionária, já confinada a
    /// [-0.5, 0.5], passa pelo polinômio.
    /// </summary>
    private static double Exp2(double x)
    {
        double k = Math.Floor(x + 0.5);
        double f = x - k;

        double p = E12;
        p = (p * f) + E11;
        p = (p * f) + E10;
        p = (p * f) + E9;
        p = (p * f) + E8;
        p = (p * f) + E7;
        p = (p * f) + E6;
        p = (p * f) + E5;
        p = (p * f) + E4;
        p = (p * f) + E3;
        p = (p * f) + E2;
        p = (p * f) + E1;
        p = (p * f) + E0;

        return p * Pow2((int)k);
    }

    /// <summary>
    /// <c>2^k</c> exato, montando o campo de expoente do <see cref="double"/> diretamente. Não há
    /// aritmética envolvida, logo não há erro. <paramref name="k"/> vem do domínio já validado de
    /// <see cref="Pow10"/>, que o mantém dentro da faixa normalizada.
    /// </summary>
    private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

    /// <summary>
    /// Reduz o ângulo ao quadrante e a um resto em [-π/4, π/4], onde as séries convergem rápido.
    /// </summary>
    private static (int Quadrant, double Remainder) Reduce(double radians)
    {
        if (!(Math.Abs(radians) <= MaxAngleRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(radians), radians,
                $"Sin/Cos support |radians| <= {MaxAngleRadians}.");
        }

        double n = Math.Floor((radians * TwoOverPi) + 0.5);

        // Três subtrações em vez de uma: cada pedaço de π/2 remove o erro que o anterior deixou.
        double r = radians - (n * HalfPiHigh);
        r -= n * HalfPiMid;
        r -= n * HalfPiLow;

        // & 3 sobre o int com sinal já dá o quadrante certo para n negativo (complemento de dois).
        return ((int)n & 3, r);
    }

    private static double SinKernel(double r)
    {
        double r2 = r * r;

        double p = S15;
        p = (p * r2) + S13;
        p = (p * r2) + S11;
        p = (p * r2) + S9;
        p = (p * r2) + S7;
        p = (p * r2) + S5;
        p = (p * r2) + S3;

        return r + ((r * r2) * p);
    }

    private static double CosKernel(double r)
    {
        double r2 = r * r;

        double p = C16;
        p = (p * r2) + C14;
        p = (p * r2) + C12;
        p = (p * r2) + C10;
        p = (p * r2) + C8;
        p = (p * r2) + C6;
        p = (p * r2) + C4;
        p = (p * r2) + C2;

        return 1.0 + (r2 * p);
    }
}
