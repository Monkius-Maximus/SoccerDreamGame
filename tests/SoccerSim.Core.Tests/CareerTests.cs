using System.Reflection;
using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class CareerTests
{
    // A shared in-memory DB lives only while at least one connection is open, so each
    // test holds a keep-alive connection for the duration.
    private static (SqliteConnectionFactory Factory, SqliteConnection KeepAlive) NewMigratedDb(bool includeSeeds)
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate(includeSeeds);
        return (factory, keepAlive);
    }

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
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(includeSeeds: true);
        using SqliteConnection connection = keepAlive;

        CareerState? career = new SqliteCareerService(connection).GetActiveCareer();

        Assert.NotNull(career);
        Assert.Equal(1, career!.HumanPlayerId);
        Assert.Equal(1, career.HumanTeamId);                  // player 1 plays for Riverside FC (team 1)
        Assert.Equal(85, career.TraitWeights["aggression"]);  // seeded hot_headed + showboat
        Assert.Equal(80, career.TraitWeights["selfishness"]);
        Assert.Equal(0xD1CED00D2026UL, career.MasterSeed);    // stated by the seed, never defaulted
    }

    /// <summary>
    /// The seed is what makes the save replayable, so it has to survive the database round trip
    /// exactly. SQLite integers are signed 64-bit; a seed with the high bit set is the case that
    /// would break a naive conversion, so it is the one worth pinning.
    /// </summary>
    [Fact]
    public void GetActiveCareer_RoundTripsASeedWithTheHighBitSet()
    {
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(includeSeeds: true);
        using SqliteConnection connection = keepAlive;

        const ulong seed = 0xFEDCBA9876543210UL;
        using (SqliteCommand update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE Career SET MasterSeed = $seed WHERE Id = 1;";
            update.Parameters.AddWithValue("$seed", unchecked((long)seed));
            update.ExecuteNonQuery();
        }

        Assert.Equal(seed, new SqliteCareerService(connection).GetActiveCareer()!.MasterSeed);
    }

    /// <summary>
    /// Fail-fast: a career with no seed has no world to replay. Substituting a default here would
    /// silently generate a different world than the save was written against.
    /// </summary>
    [Fact]
    public void GetActiveCareer_WithoutAMasterSeed_Throws()
    {
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(includeSeeds: true);
        using SqliteConnection connection = keepAlive;

        using (SqliteCommand clear = connection.CreateCommand())
        {
            clear.CommandText = "UPDATE Career SET MasterSeed = NULL WHERE Id = 1;";
            clear.ExecuteNonQuery();
        }

        var service = new SqliteCareerService(connection);
        Assert.Throws<InvalidOperationException>(() => { service.GetActiveCareer(); });
    }

    /// <summary>
    /// A constante de backfill da migração 0006 é a seed com que os mundos antigos foram de fato
    /// gerados (o antigo constante do GameBootstrap). Se ela derivar, todo save pré-migração passa
    /// a replayar um mundo diferente — em silêncio. Por isso está pinada aqui.
    /// </summary>
    [Fact]
    public void Migration0006_BackfillsWithTheExactLegacyBootstrapConstant()
    {
        Assembly assembly = typeof(MigrationRunner).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("0006_career_master_seed.sql", StringComparison.Ordinal));

        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        string sql = reader.ReadToEnd();

        Assert.Contains(
            $"UPDATE Career SET MasterSeed = {0xD1CED00D2026UL} WHERE MasterSeed IS NULL;",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GetActiveCareer_WithoutSeed_ReturnsNull()
    {
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(includeSeeds: false);
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
