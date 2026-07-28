using SoccerSim.Core.Events;
using SoccerSim.Core.Time;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class EventTriggerTests
{
    private static readonly DateTime Start = new(2026, 8, 1, 0, 0, 0);

    private static EventDefinition Def(
        string key,
        EventTier tier,
        double probability,
        IReadOnlyDictionary<string, double>? traitModifiers = null) =>
        new(key, tier, probability, traitModifiers ?? new Dictionary<string, double>());

    [Fact]
    public void RollForDay_BaseProbabilityOne_Fires()
    {
        var manager = new EventManager(new[] { Def("flight_delay", EventTier.Low, 1.0) });
        var context = new EventRollContext(1, new Dictionary<string, int>(), 1.0, new StubRandom(0.0));

        GameEvent? rolled = manager.RollForDay(Start, context);

        Assert.NotNull(rolled);
        Assert.Equal(EventTier.Low, rolled!.Tier);
    }

    [Fact]
    public void RollForDay_TraitWeight_CrossesThreshold()
    {
        var manager = new EventManager(new[]
        {
            Def("dressing_room_bust_up", EventTier.Medium, 0.10,
                new Dictionary<string, double> { ["aggression"] = 1.0 }),
        });

        // p = 0.10, roll 0.5 -> no fire.
        GameEvent? without = manager.RollForDay(Start,
            new EventRollContext(1, new Dictionary<string, int>(), 1.0, new StubRandom(0.5)));

        // p = 0.10 + (50/100 * 1.0) = 0.60, roll 0.5 -> fires.
        GameEvent? with = manager.RollForDay(Start,
            new EventRollContext(1, new Dictionary<string, int> { ["aggression"] = 50 }, 1.0, new StubRandom(0.5)));

        Assert.Null(without);
        Assert.NotNull(with);
    }

    [Fact]
    public void AdvanceCalendar_HighEvent_InterruptsThenResumesToTarget()
    {
        var clock = new GameClock(Start);
        var events = new EventManager(new[] { Def("contract_offer", EventTier.High, 1.0) });
        var lod = new NullLodManager();

        // Fires on the first roll only; every later roll returns 1.0 (no fire).
        var rng = new ScriptedRandom(0.0);
        EventRollContext Factory(DateTime date) => new(1, new Dictionary<string, int>(), 1.0, rng);
        var manager = new TimeManager(clock, events, lod, Factory, TestStreams.LifeEvents());

        DateTime target = Start.AddDays(5);
        TimeAdvanceResult interrupted = manager.AdvanceCalendar(target);

        Assert.True(interrupted.Interrupted);
        Assert.False(interrupted.Completed);
        Assert.NotNull(interrupted.PendingEvent);
        Assert.Equal(EventTier.High, interrupted.PendingEvent!.Tier);
        Assert.NotEqual(Guid.Empty, interrupted.ResumeToken);
        Assert.False(manager.IsCalendarSimulating);
        Assert.Equal(TimeSpeed.Paused, manager.Speed);

        TimeAdvanceResult resumed = manager.ResumeCalendar(interrupted.ResumeToken, target);

        Assert.True(resumed.Completed);
        Assert.False(resumed.Interrupted);
        Assert.Equal(target, resumed.ReachedDate);
    }

    [Fact]
    public void ResumeCalendar_WithStaleToken_Throws()
    {
        var clock = new GameClock(Start);
        var events = new EventManager(Array.Empty<EventDefinition>());
        var manager = new TimeManager(clock, events, new NullLodManager(),
            _ => new EventRollContext(1, new Dictionary<string, int>(), 1.0, new StubRandom(1.0)),
            TestStreams.LifeEvents());

        Assert.Throws<InvalidOperationException>(
            () => manager.ResumeCalendar(Guid.NewGuid(), Start.AddDays(1)));
    }
}
