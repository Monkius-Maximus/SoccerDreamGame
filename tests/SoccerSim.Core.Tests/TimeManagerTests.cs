using SoccerSim.Core.Events;
using SoccerSim.Core.Time;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class TimeManagerTests
{
    private static readonly DateTime Start = new(2026, 8, 1, 0, 0, 0);

    private static TimeManager CreateManager(
        out NullLodManager lod,
        IEnumerable<EventDefinition>? definitions = null,
        IRandom? rng = null)
    {
        lod = new NullLodManager();
        var clock = new GameClock(Start);
        var events = new EventManager(definitions ?? Array.Empty<EventDefinition>());
        IRandom random = rng ?? new StubRandom(1.0); // 1.0 => never fires
        NullLodManager captured = lod;

        EventRollContext Factory(DateTime date) =>
            new(1, new Dictionary<string, int>(), 1.0, random);

        return new TimeManager(clock, events, captured, Factory);
    }

    [Fact]
    public void Tick_Normal_AdvancesSixtyMinutesPerSecond()
    {
        TimeManager manager = CreateManager(out _);
        manager.SetSpeed(TimeSpeed.Normal);
        manager.Tick(1.0);
        Assert.Equal(Start.AddMinutes(60), manager.CurrentDate);
    }

    [Fact]
    public void Tick_Quadruple_AdvancesFourTimesNormal()
    {
        TimeManager manager = CreateManager(out _);
        manager.SetSpeed(TimeSpeed.Quadruple);
        manager.Tick(1.0);
        Assert.Equal(Start.AddMinutes(240), manager.CurrentDate);
    }

    [Fact]
    public void Tick_Paused_DoesNotAdvance()
    {
        TimeManager manager = CreateManager(out _);
        manager.SetSpeed(TimeSpeed.Paused);
        manager.Tick(10.0);
        Assert.Equal(Start, manager.CurrentDate);
    }

    [Fact]
    public void SkipToTaskCompletion_JumpsClockAndCompletes()
    {
        TimeManager manager = CreateManager(out _);
        DateTime taskEnd = Start.AddHours(5);

        TimeAdvanceResult result = manager.SkipToTaskCompletion(taskEnd);

        Assert.Equal(taskEnd, manager.CurrentDate);
        Assert.True(result.Completed);
        Assert.False(result.Interrupted);
    }

    [Fact]
    public void AdvanceCalendar_NoEvents_ReachesTargetAndRaisesDayElapsed()
    {
        TimeManager manager = CreateManager(out NullLodManager lod);
        int dayEvents = 0;
        manager.DayElapsed += _ => dayEvents++;
        DateTime target = Start.AddDays(3);

        TimeAdvanceResult result = manager.AdvanceCalendar(target);

        Assert.True(result.Completed);
        Assert.False(result.Interrupted);
        Assert.Equal(target, result.ReachedDate);
        Assert.Equal(3, dayEvents);
        Assert.Equal(3, lod.DayCount);
    }
}
