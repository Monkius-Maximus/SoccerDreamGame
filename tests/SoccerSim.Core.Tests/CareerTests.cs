using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class CareerTests
{
    private static (SqliteConnectionFactory Factory, SqliteConnection KeepAlive) NewMigratedDb(bool withContent)
        => TestWorld.New(withContent);

    [Fact]
    public void TraitWeights_TakeStrongestDimensionAcrossTraits()
    {
        var traits = new[]
        {
            new PlayerTrait(1, "hot_headed", "Hot-Headed", 85, 40, 10),
            new PlayerTrait(3, "showboat", "Showboat", 50, 80, 15),
        };

        IReadOnlyDictionary<string, int> weights = PlayerTraitWeights.From(traits);

        Assert.Equal(85, weights["aggression"]);  // hot_headed (85) beats showboat (50)
        Assert.Equal(80, weights["selfishness"]); // showboat (80) beats hot_headed (40)
    }

    [Fact]
    public void TraitWeights_NoTraits_IsEmpty()
    {
        Assert.Empty(PlayerTraitWeights.From(Array.Empty<PlayerTrait>()));
    }

    [Fact]
    public void GetActiveCareer_ResolvesSeededHuman_PlayerTeamAndTraitWeights()
    {
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(withContent: true);
        using SqliteConnection connection = keepAlive;

        CareerState? career = new SqliteCareerService(connection).GetActiveCareer();

        Assert.NotNull(career);
        Assert.Equal(1, career!.HumanPlayerId);
        Assert.Equal(1, career.HumanTeamId);                  // player 1 plays for Riverside FC (team 1)
        Assert.Equal(85, career.TraitWeights["aggression"]);  // seeded hot_headed + showboat
        Assert.Equal(80, career.TraitWeights["selfishness"]);
    }

    [Fact]
    public void GetActiveCareer_WithoutSeed_ReturnsNull()
    {
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(withContent: false);
        using SqliteConnection connection = keepAlive;

        Assert.Null(new SqliteCareerService(connection).GetActiveCareer());
    }

    [Fact]
    public void CareerTraitWeights_RaiseEventProbability_EndToEnd()
    {
        // press_conference keys off "aggression"; the human's trait weight should lift its
        // probability above the bare base rate. Mirrors GameBootstrap's definition + roll wiring.
        var manager = new EventManager(new[]
        {
            new EventDefinition("press_conference", EventTier.Medium, 0.03,
                new Dictionary<string, double> { ["aggression"] = 0.05 }),
        });

        IReadOnlyDictionary<string, int> human = PlayerTraitWeights.From(new[]
        {
            new PlayerTrait(1, "hot_headed", "Hot-Headed", 85, 40, 10),
        });

        // base p = 0.03; with aggression 85: p = 0.03 + (85/100 * 0.05) = 0.0725.
        // A roll of 0.05 fires only when the trait weight is applied.
        GameEvent? withTrait = manager.RollForDay(new DateTime(2026, 8, 1),
            new EventRollContext(1, human, 1.0, new StubRandom(0.05)));
        GameEvent? without = manager.RollForDay(new DateTime(2026, 8, 1),
            new EventRollContext(1, new Dictionary<string, int>(), 1.0, new StubRandom(0.05)));

        Assert.NotNull(withTrait);
        Assert.Null(without);
    }
}
