namespace SoccerSim.Core.Random;

/// <summary>
/// Deriva a seed de um stream isolado a partir da master seed do mundo, do nome do stream e de
/// uma lista ordenada de ids (clube, jogador, fixture, temporada, rodada…).
///
/// <para>
/// <b>ESTA COMPOSIÇÃO ESTÁ CONGELADA.</b> Ela é lida toda vez que um save é reaberto: mudar a
/// ordem das operações, uma constante, o hash do nome ou a forma como os ids entram produz seeds
/// diferentes para as mesmas entradas e <b>invalida todo save existente</b> — o mundo é
/// regenerado diferente e o replay para de bater. Se um dia precisar mudar, mude com um número
/// de versão novo e um caminho de migração explícito, nunca editando o que está aqui.
/// </para>
///
/// <para>A composição, exatamente:</para>
/// <code>
/// seed = Mix(masterSeed)
/// seed = Mix(seed ^ FnvHash(nome do stream))
/// para cada id, na ordem:  seed = Mix(seed ^ (ulong)id)
/// </code>
///
/// <para>
/// Todo XOR é seguido de um <see cref="SplitMix64.Mix"/>. Isso importa: XOR sozinho é
/// comutativo e associativo, então sem a mistura a cada passo <c>(a, b)</c> e <c>(b, a)</c>
/// colidiriam e ids próximos gerariam streams próximos. Com o Mix intercalado, a ordem dos ids
/// é significativa e ids vizinhos ficam bem separados.
/// </para>
/// </summary>
public static class SeedDerivation
{
    // FNV-1a de 64 bits. Escolhido por ser trivial de reimplementar em qualquer linguagem
    // (ferramentas externas de calibração precisam reproduzir estas seeds) e por não depender
    // de nenhuma API de hash da plataforma — GetHashCode() de string é randomizado por processo
    // e jamais poderia entrar aqui.
    private const ulong FnvOffsetBasis = 0xCBF29CE484222325UL;
    private const ulong FnvPrime = 0x00000100000001B3UL;

    /// <summary>
    /// A seed do stream <paramref name="stream"/> para a entidade identificada por
    /// <paramref name="ids"/>, dentro do mundo <paramref name="masterSeed"/>.
    ///
    /// <para>
    /// A ordem dos ids é significativa: <c>(a, b)</c> e <c>(b, a)</c> dão streams diferentes.
    /// Os ids são <see cref="long"/> para aceitar os <see cref="int"/> do domínio sem cast em
    /// cada call site; valores negativos entram pelo padrão de bits em complemento de dois
    /// (também congelado).
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Se <paramref name="stream"/> não é um valor declarado.</exception>
    public static ulong Derive(ulong masterSeed, StreamName stream, params long[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        ulong seed = SplitMix64.Mix(masterSeed);
        seed = SplitMix64.Mix(seed ^ FnvHash(NameOf(stream)));

        foreach (long id in ids)
            seed = SplitMix64.Mix(seed ^ unchecked((ulong)id));

        return seed;
    }

    /// <summary>
    /// FNV-1a de 64 bits sobre os bytes ASCII de <paramref name="text"/>. CONGELADO junto com
    /// <see cref="Derive"/>.
    /// </summary>
    private static ulong FnvHash(string text)
    {
        unchecked
        {
            ulong hash = FnvOffsetBasis;
            foreach (char c in text)
            {
                // Os nomes de stream são ASCII por construção. A guarda existe para que a
                // codificação nunca vire uma pergunta em aberto (UTF-8? UTF-16? qual endianness?)
                // caso alguém acrescente um nome acentuado — aí falha na hora, em vez de
                // silenciosamente congelar uma seed diferente da que outra ferramenta calcularia.
                if (c > 0x7F)
                    throw new ArgumentOutOfRangeException(nameof(text), $"Stream names must be ASCII; '{text}' is not.");

                hash ^= c;
                hash *= FnvPrime;
            }

            return hash;
        }
    }

    /// <summary>
    /// O nome congelado de cada stream, escrito à mão em vez de <c>ToString()</c>: a seed depende
    /// deste literal, então ele não pode ser um efeito colateral de reflexão nem de como o
    /// compilador nomeia membros. Um valor novo no enum sem uma linha aqui <b>lança</b> — e o
    /// teste que percorre todos os membros do enum pega isso no primeiro build.
    /// </summary>
    private static string NameOf(StreamName stream) => stream switch
    {
        StreamName.WorldGeneration => "WorldGeneration",
        StreamName.ClubIdentity => "ClubIdentity",
        StreamName.PlayerGeneration => "PlayerGeneration",
        StreamName.MatchSimulation => "MatchSimulation",
        StreamName.BackgroundSimulation => "BackgroundSimulation",
        StreamName.LifeEvents => "LifeEvents",
        _ => throw new ArgumentOutOfRangeException(nameof(stream),
            $"Unknown stream '{stream}'. Add it to SeedDerivation.NameOf with a frozen name."),
    };
}
