namespace SoccerSim.Core.Random;

/// <summary>
/// Os streams de aleatoriedade do simulador. Enum <b>fechado</b> por decisão de arquitetura:
/// um stream não é conteúdo, é uma decisão sobre o que é isolado de quê. Valor fora daqui não
/// compila, e não existe overload que aceite string arbitrária — adicionar um stream exige
/// código, revisão e uma linha nova em <see cref="SeedDerivation"/>.
///
/// <para>
/// A seed de cada stream deriva do <b>nome</b> (via hash FNV-1a), não do valor ordinal. Logo,
/// reordenar ou renumerar os membros é inofensivo; <b>renomear um membro quebra todo save
/// existente</b>, porque muda a seed derivada. Os nomes são congelados.
/// </para>
///
/// <para>
/// Streams distintos são independentes: consumir valores de um nunca desloca outro. É isso que
/// permite re-simular uma partida isolada sem perturbar a geração do mundo.
/// </para>
/// </summary>
public enum StreamName
{
    /// <summary>Geração da base de mundo (países, ligas, calendário).</summary>
    WorldGeneration,

    /// <summary>Identidade de clube — nome, cores, estádio, história.</summary>
    ClubIdentity,

    /// <summary>Geração de jogadores: atributos, traços, nomes.</summary>
    PlayerGeneration,

    /// <summary>Resolvedores de partida (Tier 1 minuto a minuto e a simulação de campo).</summary>
    MatchSimulation,

    /// <summary>Simulação de fundo dos Tiers 2/3.</summary>
    BackgroundSimulation,

    /// <summary>Eventos de vida e carreira (o loop de interrupção do calendário).</summary>
    LifeEvents,
}
