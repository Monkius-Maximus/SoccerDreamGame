using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Economy;
using SoccerSim.Core.Events;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Persistence;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Covers the two seams that turned the life-sim from "proven in tests" into "load-bearing in the
/// running game": money actually leaving a wallet, and wellbeing actually landing on a player.
/// </summary>
public sealed class EconomyAndFormTests
{
    private static readonly DateTime Day = new(2026, 8, 1);

    /// <summary>Seeded player 1 (Alex Mercer) starts with 250 000 and plays for team 1.</summary>
    private const int Human = 1;

    private static SqliteConnection NewSeededDb()
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate(includeSeeds: true);
        return keepAlive;
    }

    // ── Economy ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Charge_DebitsTheBalanceAndWritesALedgerRow()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);
        long before = economy.GetBalance(Human);

        bool charged = economy.TryCharge(Human, 1_200, TransactionCategory.Recovery, Day);

        Assert.True(charged);
        Assert.Equal(before - 1_200, economy.GetBalance(Human));
        Assert.Equal(-1_200, SingleLedgerAmount(connection));
    }

    [Fact]
    public void Charge_BeyondTheBalance_ChangesNothingAtAll()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);
        long before = economy.GetBalance(Human);

        bool charged = economy.TryCharge(Human, before + 1, TransactionCategory.Leisure, Day);

        // Refused means refused: no partial debit, and no orphan ledger row claiming a spend.
        Assert.False(charged);
        Assert.Equal(before, economy.GetBalance(Human));
        Assert.Equal(0, LedgerRowCount(connection));
    }

    [Fact]
    public void Credit_RaisesTheBalance()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);
        long before = economy.GetBalance(Human);

        economy.Credit(Human, 50_000, TransactionCategory.Bonus, Day);

        Assert.Equal(before + 50_000, economy.GetBalance(Human));
    }

    [Fact]
    public void ApplyResolution_MovesTheMoneyFromAResolvedEvent()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);
        long before = economy.GetBalance(Human);

        economy.ApplyResolution(Human, new EventResolutionResult(
            Guid.NewGuid(),
            Array.Empty<StatDelta>(),
            [new ResourceDelta("money", 250_000)]));

        Assert.Equal(before + 250_000, economy.GetBalance(Human));
    }

    [Fact]
    public void UnknownPlayer_StartsAtZeroAndCannotAfford()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);

        Assert.Equal(0, economy.GetBalance(999));
        Assert.False(economy.CanAfford(999, 1));
        Assert.True(economy.CanAfford(999, 0)); // free is always affordable
    }

    // ── Activities are actually paid for ────────────────────────────────────────────────

    [Fact]
    public void PricedActivity_DebitsTheWallet()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);
        WellbeingService service = ServiceWith(economy);
        long before = economy.GetBalance(Human);

        service.Perform("physio", Day); // costs 1 200

        Assert.Equal(before - 1_200, economy.GetBalance(Human));
    }

    [Fact]
    public void FreeActivity_LeavesTheWalletAlone()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);
        WellbeingService service = ServiceWith(economy);
        long before = economy.GetBalance(Human);

        service.Perform("shower", Day);

        Assert.Equal(before, economy.GetBalance(Human));
    }

    [Fact]
    public void UnaffordableActivity_ThrowsAndLeavesTheNeedsUntouched()
    {
        using SqliteConnection connection = NewSeededDb();
        var economy = new SqliteEconomyService(connection);
        // Drain the wallet so nothing priced is reachable.
        economy.TryCharge(Human, economy.GetBalance(Human), TransactionCategory.Leisure, Day);
        WellbeingService service = ServiceWith(economy);
        double moraleBefore = service.State[NeedKind.Morale];

        Assert.False(service.CanAfford("leisure"));
        Assert.Throws<InvalidOperationException>(() => service.Perform("leisure", Day));

        // The charge happens BEFORE the needs move, so a refused payment must not have granted the
        // benefit.
        Assert.Equal(moraleBefore, service.State[NeedKind.Morale], precision: 9);
    }

    [Fact]
    public void CanAfford_IsAlwaysTrueWithoutAnEconomy() =>
        // Headless tests and a future server run the life-sim with no wallet behind it.
        Assert.True(
            new WellbeingService(
                WellbeingState.CreateDefault(CareerRole.Player), new LifeSimulator(), new StubRandom(0.5))
                .CanAfford("leisure"));

    // ── Wellbeing reaching a real, persisted player ─────────────────────────────────────

    [Fact]
    public void PlayerState_LoadsTheHumanWithAttributesAndTraits()
    {
        using SqliteConnection connection = NewSeededDb();

        Player? player = new SqlitePlayerStateService(connection).Load(Human);

        Assert.NotNull(player);
        Assert.Equal("Alex", player!.FirstName);
        Assert.Equal(1, player.TeamId);
        Assert.True(player.BaseAttributes.Pace > 0);
        Assert.NotEmpty(player.Traits); // seeded trait assignments feed the event roll
    }

    [Fact]
    public void PlayerState_ResolvesTheCurrentSeasonFromTheTeamsLeague()
    {
        using SqliteConnection connection = NewSeededDb();

        Assert.Equal(1, new SqlitePlayerStateService(connection).GetCurrentSeasonId(teamId: 1));
    }

    [Fact]
    public void FormMood_RoundTripsAndUpserts()
    {
        using SqliteConnection connection = NewSeededDb();
        var state = new SqlitePlayerStateService(connection);

        state.SaveFormMood(Human, seasonId: 1, new FormMood(-3), Day);
        state.SaveFormMood(Human, seasonId: 1, new FormMood(4), Day.AddDays(1));

        // Recalculated daily, so the second write replaces rather than appending.
        Assert.Equal(4, state.LoadFormMood(Human, 1)!.Value.Value);
    }

    [Fact]
    public void TheWholeChain_WellbeingToPersistedForm()
    {
        // What GameBootstrap.OnDayElapsed does, end to end: advance the day, sync form onto the
        // human, persist it. This is the path that was missing entirely before.
        using SqliteConnection connection = NewSeededDb();
        var playerState = new SqlitePlayerStateService(connection);
        Player human = playerState.Load(Human)!;
        int baseline = human.BaseAttributes.Pace;

        WellbeingState wrecked = WellbeingState.CreateDefault(CareerRole.Player, 10.0);
        var service = new WellbeingService(wrecked, new LifeSimulator(), new StubRandom(0.5));

        service.SyncFormMood(human);
        playerState.SaveFormMood(human.Id, seasonId: 1, human.FormMood, Day);

        Assert.Equal(-4, human.FormMood.Value);                       // index 10 -> (10-50)/10
        Assert.Equal(baseline - 4, human.EffectiveAttributes().Pace); // reached the pitch
        Assert.Equal(-4, playerState.LoadFormMood(Human, 1)!.Value.Value); // and survived a reload
    }

    private static WellbeingService ServiceWith(IEconomyService economy) =>
        new(WellbeingState.CreateDefault(CareerRole.Player),
            new LifeSimulator(),
            new StubRandom(0.5),
            repository: null,
            careerId: 1,
            economy: economy,
            walletPlayerId: Human);

    private static long SingleLedgerAmount(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Amount FROM Transactions;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long LedgerRowCount(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Transactions;";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
