using SoccerSim.Core.Events;

namespace SoccerSim.Core.Random;

/// <summary>
/// A fachada de consumo: um stream nomeado e isolado, com as distribuições que a simulação
/// realmente usa e um estado serializável.
///
/// <para>
/// Nenhum sistema instancia <see cref="SplitMix64"/> direto. Todo consumo entra por
/// <see cref="Create"/> (master seed + <see cref="StreamName"/> + ids) ou por
/// <see cref="FromState"/> (restaurar um save), e todo consumidor recebe o stream <b>injetado
/// por construtor</b> — nenhuma classe fabrica o próprio. Não há seed default implícita e não há
/// fallback: sem stream, não roda.
/// </para>
///
/// <para>
/// A superfície é deliberadamente mínima. Cada método aqui existe porque um sistema pede; não
/// se acrescenta distribuição "por precaução".
/// </para>
/// </summary>
public sealed class RandomStream : IRandom
{
    private readonly SplitMix64 _generator;

    private RandomStream(ulong seed) => _generator = new SplitMix64(seed);

    /// <summary>
    /// O stream <paramref name="stream"/> do mundo <paramref name="masterSeed"/>, para a entidade
    /// identificada por <paramref name="ids"/> (clube, jogador, fixture, temporada, rodada…).
    /// A ordem dos ids é significativa — ver <see cref="SeedDerivation"/>.
    /// </summary>
    public static RandomStream Create(ulong masterSeed, StreamName stream, params long[] ids)
        => new(SeedDerivation.Derive(masterSeed, stream, ids));

    /// <summary>
    /// Retoma um stream exatamente de onde ele parou, a partir de um <see cref="State"/> salvo.
    /// É a metade de leitura do save/load (§3.5).
    /// </summary>
    public static RandomStream FromState(ulong state) => new(state);

    /// <summary>
    /// Todo o estado do stream — um único <see cref="ulong"/>. Vai para o schema SQLite como
    /// <c>INTEGER</c>; não existe serialização binária customizada porque não há o que
    /// customizar.
    /// </summary>
    public ulong State => _generator.State;

    /// <summary>Reposiciona este stream num <see cref="State"/> salvo, sem realocar nada.</summary>
    public void RestoreState(ulong state) => _generator.State = state;

    /// <summary>A saída crua de 64 bits do gerador.</summary>
    public ulong NextUInt64() => _generator.Next();

    /// <summary>
    /// Um double em <c>[0, 1)</c> com 53 bits de mantissa — a precisão exata de um
    /// <see cref="double"/>, sem bits desperdiçados nem valores inalcançáveis. Descarta os 11
    /// bits baixos e escala por 2^-53, que é uma multiplicação exata em IEEE 754.
    /// </summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>
    /// Um int uniformemente distribuído em <c>[minInclusive, maxExclusive)</c>, <b>sem viés</b>.
    ///
    /// <para>
    /// Por rejeição com máscara: mascara o draw para a menor potência de dois que cobre o
    /// intervalo e redesenha enquanto cair fora. Todo valor do intervalo sai com exatamente a
    /// mesma probabilidade, e não há um único <c>%</c> envolvido — reduzir a saída crua por
    /// módulo introduziria viés modular (os primeiros <c>2^64 mod range</c> valores sairiam com
    /// frequência maior), e geração de mundo com distribuição enviesada não é aceitável.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se <paramref name="maxExclusive"/> não é estritamente maior que <paramref name="minInclusive"/>.
    /// Um intervalo vazio não tem resposta correta, então não se inventa uma.
    /// </exception>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                $"maxExclusive ({maxExclusive}) must be greater than minInclusive ({minInclusive}).");
        }

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        ulong mask = CoveringMask(range);

        ulong draw;
        do
        {
            draw = NextUInt64() & mask;
        }
        while (draw >= range);

        return (int)(minInclusive + (long)draw);
    }

    /// <summary>
    /// <c>true</c> com probabilidade <paramref name="probability"/>. Consome exatamente um draw,
    /// inclusive nos extremos 0 e 1, para que o stream avance de forma previsível.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se <paramref name="probability"/> está fora de <c>[0, 1]</c> (ou é NaN).</exception>
    public bool NextBool(double probability)
    {
        // Escrito como negação para que NaN — que falha toda comparação — também caia aqui.
        if (!(probability >= 0.0 && probability <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(probability),
                $"probability ({probability}) must be within [0, 1].");
        }

        return NextDouble() < probability;
    }

    /// <summary>
    /// Uma amostra aproximadamente normal, truncada em ±6σ, por Irwin–Hall com n = 12:
    /// a soma de 12 uniformes de <c>[0, 1)</c> tem média 6 e variância exatamente 1, então
    /// subtrair 6 já dá N(0, 1).
    ///
    /// <para>
    /// <b>Por que não Box–Muller nem Marsaglia polar:</b> a IEEE 754 só garante arredondamento
    /// correto para <c>+ − × ÷</c> e raiz quadrada. Logaritmo, exponencial e trigonometria
    /// <b>não</b> são garantidos, e as duas transformadas clássicas dependem deles — a mesma seed
    /// poderia divergir bit a bit entre plataformas justamente na geração de atributos, que é o
    /// pior lugar possível para perder o replay. Irwin–Hall é só soma e multiplicação: idêntico
    /// em qualquer lugar.
    /// </para>
    ///
    /// <para>
    /// <b>A cauda além de 6σ não existe por design</b>, não por acidente: o resultado está sempre
    /// em <c>[mean − 6σ, mean + 6σ)</c>. Isso é exatamente o que a geração de CA/PA por gaussiana
    /// truncada precisa, e evita ter que recortar amostras depois (recorte posterior deformaria a
    /// distribuição e consumiria um número variável de draws).
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se <paramref name="stdDev"/> é negativo ou NaN, ou <paramref name="mean"/> é NaN.</exception>
    public double NextGaussianBounded(double mean, double stdDev)
    {
        if (double.IsNaN(mean))
            throw new ArgumentOutOfRangeException(nameof(mean), "mean must be a number.");

        if (!(stdDev >= 0.0))
            throw new ArgumentOutOfRangeException(nameof(stdDev), $"stdDev ({stdDev}) must be >= 0.");

        double sum = 0.0;
        for (int i = 0; i < IrwinHallTerms; i++)
            sum += NextDouble();

        return mean + ((sum - (IrwinHallTerms / 2.0)) * stdDev);
    }

    /// <summary>
    /// Embaralha <paramref name="items"/> no lugar por Fisher–Yates, consumindo
    /// <see cref="NextInt"/>. Cada uma das n! permutações é igualmente provável — herda a ausência
    /// de viés de <see cref="NextInt"/>.
    /// </summary>
    public void Shuffle<T>(IList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = NextInt(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// Um <see cref="Guid"/> de 128 bits tirado do stream. Existe porque a simulação precisa de
    /// identificadores de entidade (eventos, tokens) que sobrevivam a um replay — o gerador de
    /// GUID do sistema é não-determinístico por definição e não pode aparecer em caminho
    /// replayável.
    ///
    /// <para>
    /// Os campos do GUID são montados explicitamente a partir de dois draws, em vez de
    /// reinterpretar um buffer de bytes, para que o resultado não dependa da endianness da
    /// máquina.
    /// </para>
    /// </summary>
    public Guid NextGuid()
    {
        ulong high = NextUInt64();
        ulong low = NextUInt64();

        // unchecked pelo mesmo motivo do gerador: estas conversões truncam de propósito, e sob
        // CheckForOverflowUnderflow cada uma delas lançaria em vez de cortar os bits altos.
        unchecked
        {
            return new Guid(
                (int)(uint)(high >> 32),
                (short)(ushort)(high >> 16),
                (short)(ushort)high,
                (byte)(low >> 56), (byte)(low >> 48), (byte)(low >> 40), (byte)(low >> 32),
                (byte)(low >> 24), (byte)(low >> 16), (byte)(low >> 8), (byte)low);
        }
    }

    /// <summary>
    /// <see cref="IRandom"/>: um int em <c>[0, maxExclusive)</c>. Mesmo contrato de
    /// <see cref="NextInt"/> — <paramref name="maxExclusive"/> não positivo lança.
    /// </summary>
    public int Next(int maxExclusive) => NextInt(0, maxExclusive);

    /// <summary>Número de uniformes somadas em <see cref="NextGaussianBounded"/>. n = 12 dá variância exatamente 1.</summary>
    private const int IrwinHallTerms = 12;

    /// <summary>A menor máscara <c>2^k − 1</c> que cobre <c>[0, range)</c>.</summary>
    private static ulong CoveringMask(ulong range)
    {
        ulong mask = range - 1;
        mask |= mask >> 1;
        mask |= mask >> 2;
        mask |= mask >> 4;
        mask |= mask >> 8;
        mask |= mask >> 16;
        mask |= mask >> 32;
        return mask;
    }
}
