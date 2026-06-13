using SoccerSim.Core.Events;

namespace SoccerSim.Core.Time;

/// <summary>
/// Owns the active scene's clock and the background calendar simulation. The
/// concrete implementation is a plain POCO; a thin Godot autoload drives
/// <see cref="Tick"/> from <c>_Process(delta)</c> so engine frame-time flows in
/// while the simulation logic stays engine-agnostic.
/// </summary>
public interface ITimeManager
{
    DateTime CurrentDate { get; }

    TimeSpeed Speed { get; }

    /// <summary>True while a calendar advance is running (rendered scenes are bypassed).</summary>
    bool IsCalendarSimulating { get; }

    void SetSpeed(TimeSpeed speed);

    /// <summary>Called every frame by the engine with the real frame delta in seconds.</summary>
    void Tick(double realDeltaSeconds);

    /// <summary>
    /// Task skipping: jump the clock straight to a known completion time so the
    /// caller can apply the resulting stat/resource changes instantly. No
    /// per-day simulation is run.
    /// </summary>
    TimeAdvanceResult SkipToTaskCompletion(DateTime taskEnd);

    /// <summary>
    /// Calendar simulation: advance day-by-day with no rendered scene, driving
    /// LOD match resolution and rolling life-sim events each step. Returns early
    /// with <see cref="TimeAdvanceResult.Interrupted"/> when a High/Medium event
    /// fires; the result carries the resume token.
    /// </summary>
    TimeAdvanceResult AdvanceCalendar(DateTime target, CancellationToken cancellationToken = default);

    /// <summary>Continue an interrupted calendar advance once its event was resolved.</summary>
    TimeAdvanceResult ResumeCalendar(Guid resumeToken, DateTime target, CancellationToken cancellationToken = default);

    /// <summary>Raised once per simulated day during a calendar advance.</summary>
    event Action<DateTime>? DayElapsed;

    /// <summary>Raised on week boundaries (drives Tier 2 weekly form / Tier 3 week-end math).</summary>
    event Action<DateTime>? WeekElapsed;

    /// <summary>Raised when a calendar advance is interrupted by a High/Medium event.</summary>
    event Action<TimeAdvanceResult>? AdvanceInterrupted;
}
