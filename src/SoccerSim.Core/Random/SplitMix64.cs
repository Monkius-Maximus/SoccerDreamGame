namespace SoccerSim.Core.Random;

/// <summary>
/// SplitMix64 (Sebastiano Vigna, 2015) — implementação própria em C# escrita a partir do
/// algoritmo de referência, que é dedicado a domínio público (CC0). Nenhuma dependência externa.
///
/// <para>
/// É o <b>único</b> gerador do projeto. Tudo é aritmética inteira de 64 bits com constantes
/// fixas, então a sequência é idêntica bit a bit em qualquer plataforma e qualquer versão de
/// runtime — o que o gerador da biblioteca padrão não garante, e é justamente por isso que ele
/// não entra em nada replayável.
/// </para>
///
/// <para>
/// O gerador não conhece domínio: ele só produz bits. Distribuições, streams nomeados e
/// derivação de seed vivem em <see cref="RandomStream"/> e <see cref="SeedDerivation"/>.
/// </para>
/// </summary>
public sealed class SplitMix64
{
    /// <summary>
    /// O incremento de estado (a "golden gamma", 2^64 / φ arredondado para ímpar). Somar uma
    /// constante ímpar módulo 2^64 percorre todos os 2^64 estados antes de repetir, o que dá ao
    /// gerador período máximo.
    /// </summary>
    public const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    /// <param name="seed">A seed do stream. A mesma seed sempre produz a mesma sequência.</param>
    public SplitMix64(ulong seed) => _state = seed;

    /// <summary>
    /// O estado completo do gerador — um único <see cref="ulong"/>, e nada mais. Ler e reatribuir
    /// este valor reposiciona o stream exatamente onde ele estava (§3.5: save/load).
    /// </summary>
    public ulong State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>
    /// Avança o estado e devolve a saída crua de 64 bits.
    ///
    /// <para>
    /// O <c>unchecked</c> é <b>obrigatório</b>, não decorativo: o algoritmo depende de overflow
    /// com wraparound tanto na soma da gamma quanto nas duas multiplicações do
    /// <see cref="Mix"/>. Marcá-lo explicitamente deixa o gerador imune a alguém ligar
    /// <c>CheckForOverflowUnderflow</c> em <c>Directory.Build.props</c> um dia — nesse cenário,
    /// sem o bloco, toda multiplicação lançaria <see cref="OverflowException"/> em runtime.
    /// </para>
    /// </summary>
    public ulong Next()
    {
        unchecked
        {
            _state += GoldenGamma;
            return Mix(_state);
        }
    }

    /// <summary>
    /// A função finalizadora do SplitMix64: um misturador bijetivo de 64→64 bits (as três linhas
    /// de mistura, <b>sem</b> o incremento do estado). Difunde cada bit de entrada por toda a
    /// saída, então seeds vizinhas geram streams bem separados.
    ///
    /// <para>
    /// É também o tijolo de <see cref="SeedDerivation"/>. Alterar qualquer constante aqui muda
    /// todo mundo abaixo — <b>congelado</b>.
    /// </para>
    /// </summary>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
