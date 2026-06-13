using SoccerSim.Core.Events;

namespace SoccerSim.Core.Time;

/// <summary>
/// Global time multiplier. The integer value IS the multiplier applied to the
/// real-time delta (Paused = 0 freezes the clock). Normal/2x/4x map directly to
/// the GDD time controls.
/// </summary>
public enum TimeSpeed
{
    Paused = 0,
    Normal = 1,
    Double = 2,
    Quadruple = 4,
}

/// <summary>
/// Outcome of advancing time. When <see cref="Interrupted"/> is true an event
/// halted a calendar simulation: <see cref="PendingEvent"/> carries it and
/// <see cref="ResumeToken"/> must be passed back to
/// <see cref="ITimeManager.ResumeCalendar"/> to continue.
/// </summary>
public readonly record struct TimeAdvanceResult(
    DateTime ReachedDate,
    bool Completed,
    bool Interrupted,
    GameEvent? PendingEvent,
    Guid ResumeToken);

/// <summary>
/// Pure in-game clock. Accumulates fractional in-game minutes from a speed-scaled
/// real-time delta so 2x/4x never lose precision. Has no engine dependency, which
/// is what lets the whole time system be unit-tested headlessly.
/// </summary>
public sealed class GameClock
{
    private double _carryMinutes;

    public GameClock(DateTime start) => Current = start;

    public DateTime Current { get; private set; }

    /// <summary>In-game minutes that elapse per real second at Normal (1x) speed.</summary>
    public double MinutesPerRealSecond { get; init; } = 60.0;

    public void Set(DateTime date)
    {
        Current = date;
        _carryMinutes = 0;
    }

    /// <summary>Advance the clock by a real-time delta scaled by the speed multiplier.</summary>
    public void Tick(double realDeltaSeconds, TimeSpeed speed)
    {
        int multiplier = (int)speed;
        if (multiplier <= 0 || realDeltaSeconds <= 0)
            return;

        _carryMinutes += realDeltaSeconds * MinutesPerRealSecond * multiplier;
        if (_carryMinutes < 1.0)
            return;

        int wholeMinutes = (int)_carryMinutes;
        _carryMinutes -= wholeMinutes;
        Current = Current.AddMinutes(wholeMinutes);
    }
}
