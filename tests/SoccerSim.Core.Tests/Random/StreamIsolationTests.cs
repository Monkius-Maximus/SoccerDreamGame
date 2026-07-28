using SoccerSim.Core.Random;
using Xunit;

namespace SoccerSim.Core.Tests.Random;

/// <summary>
/// O contrato de <see cref="SeedDerivation"/>: streams nomeados são reprodutíveis e isolados.
/// </summary>
public sealed class StreamIsolationTests
{
    private const ulong Master = 0xD1CED00D2026UL;

    private static SplitMix64 Stream(StreamName name, params long[] ids)
        => new(SeedDerivation.Derive(Master, name, ids));

    /// <summary>
    /// Valores congelados. Esta é a prova real do critério "mesma seed em processos separados":
    /// não basta chamar <c>Derive</c> duas vezes na mesma execução — isso só provaria que a função
    /// é determinística <i>dentro</i> deste processo. Bater contra uma constante escrita à mão
    /// prova que a composição não mudou desde o dia em que foi congelada, o que é a mesma coisa
    /// que qualquer outro processo (ou ferramenta externa) obteria.
    /// </summary>
    [Theory]
    [InlineData(0xD1CED00D2026UL, StreamName.ClubIdentity, 42L, 0xBE6B3960ED870686UL)]
    [InlineData(1UL, StreamName.ClubIdentity, -1L, 0x7764C3C746352179UL)]
    public void Derive_WithId_MatchesFrozenComposition(ulong master, StreamName name, long id, ulong expected)
        => Assert.Equal(expected, SeedDerivation.Derive(master, name, id));

    [Theory]
    [InlineData(0xD1CED00D2026UL, StreamName.LifeEvents, 0xDBFAEA198DF69411UL)]
    [InlineData(0UL, StreamName.WorldGeneration, 0x881CFC3630CEE81CUL)]
    public void Derive_WithoutIds_MatchesFrozenComposition(ulong master, StreamName name, ulong expected)
        => Assert.Equal(expected, SeedDerivation.Derive(master, name));

    [Fact]
    public void Derive_MultipleIds_MatchesFrozenComposition()
        => Assert.Equal(0x4407901D0F73E72EUL, SeedDerivation.Derive(1UL, StreamName.MatchSimulation, 2026, 7, 3));

    [Fact]
    public void Derive_IsPure()
    {
        // Chamada duas vezes com as mesmas entradas: mesma seed, sem estado escondido.
        Assert.Equal(
            SeedDerivation.Derive(Master, StreamName.ClubIdentity, 17),
            SeedDerivation.Derive(Master, StreamName.ClubIdentity, 17));
    }

    [Fact]
    public void SameMasterSeed_EveryStreamNameIsDistinct()
    {
        StreamName[] names = Enum.GetValues<StreamName>();
        var seeds = new Dictionary<ulong, StreamName>();

        foreach (StreamName name in names)
        {
            // Também é a guarda contra um valor novo no enum sem entrada em SeedDerivation.NameOf:
            // esse caso lança aqui, no primeiro build depois da mudança.
            ulong seed = SeedDerivation.Derive(Master, name);

            Assert.False(seeds.TryGetValue(seed, out StreamName clash),
                $"Streams '{name}' e '{clash}' derivam a mesma seed {seed:X16}.");
            seeds[seed] = name;
        }

        Assert.Equal(names.Length, seeds.Count);
    }

    [Fact]
    public void DifferentStreamNames_ProduceDifferentSequences()
    {
        SplitMix64 world = Stream(StreamName.WorldGeneration);
        SplitMix64 players = Stream(StreamName.PlayerGeneration);

        bool diverged = false;
        for (int i = 0; i < 16; i++)
            diverged |= world.Next() != players.Next();

        Assert.True(diverged);
    }

    [Fact]
    public void DifferentIds_ProduceDifferentSequences()
    {
        SplitMix64 club10 = Stream(StreamName.ClubIdentity, 10);
        SplitMix64 club11 = Stream(StreamName.ClubIdentity, 11);

        bool diverged = false;
        for (int i = 0; i < 16; i++)
            diverged |= club10.Next() != club11.Next();

        Assert.True(diverged);
    }

    [Fact]
    public void IdOrderMatters()
        => Assert.NotEqual(
            SeedDerivation.Derive(Master, StreamName.MatchSimulation, 3, 7),
            SeedDerivation.Derive(Master, StreamName.MatchSimulation, 7, 3));

    /// <summary>
    /// O ponto todo do isolamento: drenar um stream não desloca nenhum outro. Sem isso,
    /// re-simular uma partida mudaria a próxima geração de jogador.
    /// </summary>
    [Fact]
    public void DrainingOneStream_DoesNotDisturbAnother()
    {
        ulong expected = Stream(StreamName.PlayerGeneration, 7).Next();

        SplitMix64 noisy = Stream(StreamName.MatchSimulation, 7);
        for (int i = 0; i < 1000; i++)
            noisy.Next();

        Assert.Equal(expected, Stream(StreamName.PlayerGeneration, 7).Next());
    }

    [Fact]
    public void Derive_UndeclaredStreamName_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => { SeedDerivation.Derive(Master, (StreamName)999); });
}
