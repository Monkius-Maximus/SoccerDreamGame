using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Persistence;
using Xunit;

namespace SoccerSim.Core.Tests;

/// <summary>
/// Proves the need gauges are not decorative: each of these pins one of the existing systems that
/// consumes <see cref="WellbeingSnapshot"/>, using the real production types on both sides.
/// </summary>
public sealed class WellbeingIntegrationTests
{
    private static readonly DateTime Day = new(2026, 8, 1);

    private static Player NewPlayer() => new()
    {
        Id = 1,
        FirstName = "Alex",
        LastName = "Mercer",
        TeamId = 1,
        BaseAttributes = new PlayerAttributes(10, 10, 10, 10, 10, 10, 10),
    };

    private static WellbeingService ServiceFor(
        WellbeingState state,
        IWellbeingRepository? repository = null) =>
        new(state, new LifeSimulator(), new StubRandom(0.5), repository);

    /// <summary>Records what was written so the service's write-through can be asserted.</summary>
    private sealed class RecordingWellbeingRepository : IWellbeingRepository
    {
        public int SaveCount { get; private set; }

        public List<(DateTime Date, ActivityOutcome Outcome)> Logged { get; } = [];

        public WellbeingState? Load(int careerId, CareerRole role) => null;

        public void Save(int careerId, WellbeingState state) => SaveCount++;

        public void LogActivity(int careerId, DateTime date, ActivityOutcome outcome) =>
            Logged.Add((date, outcome));
    }

    // ── Wellbeing reaches the pitch through the EXISTING FormMood path ──────────────────

    [Fact]
    public void SyncFormMood_PoorWellbeing_LowersEffectiveAttributes()
    {
        Player player = NewPlayer();
        // Index 30 -> FormModifier -2, applied to every attribute by the existing WithModifier path.
        ServiceFor(WellbeingState.CreateDefault(CareerRole.Player, 30.0)).SyncFormMood(player);

        PlayerAttributes effective = player.EffectiveAttributes();

        Assert.Equal(-2, player.FormMood.Value);
        Assert.Equal(8, effective.Pace);
        Assert.Equal(8, effective.Shooting);
        Assert.Equal(10, player.BaseAttributes.Pace); // static base is untouched
    }

    [Fact]
    public void SyncFormMood_GoodWellbeing_RaisesEffectiveAttributes()
    {
        Player player = NewPlayer();
        ServiceFor(WellbeingState.CreateDefault(CareerRole.Player, 90.0)).SyncFormMood(player);

        Assert.Equal(4, player.FormMood.Value);       // index 90 -> (90-50)/10
        Assert.Equal(14, player.EffectiveAttributes().Pace);
    }

    [Fact]
    public void SyncFormMood_Sets_RatherThanAccumulates()
    {
        Player player = NewPlayer();
        var service = ServiceFor(WellbeingState.CreateDefault(CareerRole.Player, 90.0));

        service.SyncFormMood(player);
        service.SyncFormMood(player);
        service.SyncFormMood(player);

        // Wellbeing DEFINES form; repeated syncs must not drift it toward the +5 clamp.
        Assert.Equal(4, player.FormMood.Value);
    }

    [Fact]
    public void SyncFormMood_ArcadeMode_StillBypassesForm()
    {
        Player player = NewPlayer();
        ServiceFor(WellbeingState.CreateDefault(CareerRole.Player, 0.0)).SyncFormMood(player);

        // applyForm: false is the arcade path — the life-sim must not leak into it.
        Assert.Equal(10, player.EffectiveAttributes(applyForm: false).Pace);
    }

    // ── Wellbeing reaches the EXISTING event roll through GlobalProbabilityMultiplier ───

    [Fact]
    public void EventProbabilityMultiplier_StrugglingCareer_FiresEventsAHealthyOneDoesNot()
    {
        var manager = new EventManager(new[]
        {
            new EventDefinition("dressing_room_bust_up", EventTier.Medium, 0.10,
                new Dictionary<string, double>()),
        });

        double thriving = ServiceFor(WellbeingState.CreateDefault(CareerRole.Player, 100.0))
            .EventProbabilityMultiplier;
        double struggling = ServiceFor(WellbeingState.CreateDefault(CareerRole.Player, 0.0))
            .EventProbabilityMultiplier;

        // p_thriving  = 0.10 × 0.75 = 0.075  -> a 0.15 roll does not fire.
        // p_struggling = 0.10 × 1.75 = 0.175 -> the same roll does fire.
        GameEvent? whenThriving = manager.RollForDay(Day,
            new EventRollContext(1, new Dictionary<string, int>(), thriving, new StubRandom(0.15)));
        GameEvent? whenStruggling = manager.RollForDay(Day,
            new EventRollContext(1, new Dictionary<string, int>(), struggling, new StubRandom(0.15)));

        Assert.Null(whenThriving);
        Assert.NotNull(whenStruggling);
    }

    // ── Service behaviour ───────────────────────────────────────────────────────────────

    [Fact]
    public void AdvanceDay_WritesThroughToPersistenceAndRepublishesTheSnapshot()
    {
        var repository = new RecordingWellbeingRepository();
        WellbeingService service = ServiceFor(WellbeingState.CreateDefault(CareerRole.Player), repository);
        WellbeingSnapshot? published = null;
        service.Changed += snapshot => published = snapshot;

        double before = service.Snapshot.Index;
        service.AdvanceDay(Day);

        Assert.Equal(1, repository.SaveCount);
        Assert.NotNull(published);
        Assert.True(service.Snapshot.Index < before);
        Assert.Equal(service.Snapshot, published!.Value);
    }

    [Fact]
    public void Perform_LogsTheActivityWithItsCost()
    {
        var repository = new RecordingWellbeingRepository();
        WellbeingService service = ServiceFor(WellbeingState.CreateDefault(CareerRole.Player), repository);

        ActivityOutcome outcome = service.Perform("physio", Day);

        (DateTime date, ActivityOutcome logged) = Assert.Single(repository.Logged);
        Assert.Equal(Day, date);
        Assert.Equal("physio", logged.ActivityKey);
        Assert.Equal(1_200, outcome.Cost);
    }

    [Fact]
    public void Perform_UnknownActivity_Throws() =>
        Assert.Throws<KeyNotFoundException>(
            () => ServiceFor(WellbeingState.CreateDefault(CareerRole.Manager)).Perform("moon_walk", Day));

    [Fact]
    public void Service_ExposesTheRoleItWasBuiltFor()
    {
        Assert.Equal(CareerRole.Manager, ServiceFor(WellbeingState.CreateDefault(CareerRole.Manager)).Role);
        Assert.Equal(CareerRole.Player, ServiceFor(WellbeingState.CreateDefault(CareerRole.Player)).Role);
    }

    [Fact]
    public void Service_RunsWithoutARepository()
    {
        // Headless tests and a future multiplayer server must be able to run the life-sim with no
        // database behind it.
        WellbeingService service = ServiceFor(WellbeingState.CreateDefault(CareerRole.Manager));

        service.AdvanceDay(Day);
        service.Perform("film_study", Day);

        Assert.True(service.Snapshot.Index > 0.0);
    }
}
