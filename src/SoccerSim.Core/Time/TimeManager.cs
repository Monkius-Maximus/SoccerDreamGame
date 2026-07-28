using SoccerSim.Core.Events;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.Time;

/// <summary>
/// Default <see cref="ITimeManager"/>. Implements the EventTrigger loop: each
/// simulated day fires LOD match resolution and an event roll; a High/Medium
/// event pauses the clock and returns an interruption that the presentation
/// layer resolves before calling <see cref="ResumeCalendar"/>.
/// </summary>
public sealed class TimeManager : ITimeManager
{
    private readonly GameClock _clock;
    private readonly IEventManager _events;
    private readonly ISimulationLODManager _lod;
    private readonly Func<DateTime, EventRollContext> _rollContextFactory;
    private readonly RandomStream _rng;

    private Guid _activeResumeToken = Guid.Empty;
    private TimeSpeed _speedBeforeCalendar = TimeSpeed.Normal;

    /// <param name="clock">The in-game clock POCO.</param>
    /// <param name="events">Event roller/classifier.</param>
    /// <param name="lod">Level-of-detail match resolver, driven per day/week.</param>
    /// <param name="rollContextFactory">
    /// Builds the per-day roll context (active player + trait weights + RNG). Injected
    /// so the core never reaches into persistence or the engine directly.
    /// </param>
    /// <param name="rng">
    /// The <see cref="StreamName.LifeEvents"/> stream, injected like every other consumer.
    /// It mints the resume tokens: the calendar loop is the replayable driver, so a system
    /// GUID here would make two replays of the same save differ in their output.
    /// </param>
    public TimeManager(
        GameClock clock,
        IEventManager events,
        ISimulationLODManager lod,
        Func<DateTime, EventRollContext> rollContextFactory,
        RandomStream rng)
    {
        _clock = clock;
        _events = events;
        _lod = lod;
        _rollContextFactory = rollContextFactory;
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
    }

    public DateTime CurrentDate => _clock.Current;

    public TimeSpeed Speed { get; private set; } = TimeSpeed.Normal;

    public bool IsCalendarSimulating { get; private set; }

    public event Action<DateTime>? DayElapsed;
    public event Action<DateTime>? WeekElapsed;
    public event Action<TimeAdvanceResult>? AdvanceInterrupted;

    public void SetSpeed(TimeSpeed speed) => Speed = speed;

    public void Tick(double realDeltaSeconds)
    {
        // The calendar loop owns time while it runs; ignore real-time ticks then.
        if (IsCalendarSimulating)
            return;
        _clock.Tick(realDeltaSeconds, Speed);
    }

    public TimeAdvanceResult SkipToTaskCompletion(DateTime taskEnd)
    {
        if (taskEnd > _clock.Current)
            _clock.Set(taskEnd);

        return new TimeAdvanceResult(_clock.Current, Completed: true, Interrupted: false,
            PendingEvent: null, ResumeToken: _rng.NextGuid());
    }

    public TimeAdvanceResult AdvanceCalendar(DateTime target, CancellationToken cancellationToken = default)
    {
        _activeResumeToken = _rng.NextGuid();
        _speedBeforeCalendar = Speed == TimeSpeed.Paused ? TimeSpeed.Normal : Speed;
        return RunCalendarLoop(target, cancellationToken);
    }

    public TimeAdvanceResult ResumeCalendar(Guid resumeToken, DateTime target, CancellationToken cancellationToken = default)
    {
        if (resumeToken == Guid.Empty || resumeToken != _activeResumeToken)
            throw new InvalidOperationException("Resume token is stale or invalid; start a new AdvanceCalendar.");

        return RunCalendarLoop(target, cancellationToken);
    }

    /// <summary>
    /// The EventTrigger loop. Steps one in-game day at a time, resolves background
    /// matches via LOD, then rolls for an interrupting event. Low-stakes events are
    /// applied inline; High/Medium events pause the clock and return control.
    /// </summary>
    private TimeAdvanceResult RunCalendarLoop(DateTime target, CancellationToken cancellationToken)
    {
        IsCalendarSimulating = true;
        SetSpeed(TimeSpeed.Normal);
        try
        {
            while (_clock.Current < target)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                DateTime nextStep = _clock.Current.Date.AddDays(1);
                if (nextStep > target)
                    nextStep = target;
                _clock.Set(nextStep);

                // 1) Level-of-detail resolution for this step (pure math + SQL writes, no assets).
                DayElapsed?.Invoke(nextStep);
                _lod.OnDayElapsed(nextStep);
                if (nextStep.DayOfWeek == DayOfWeek.Monday)
                {
                    WeekElapsed?.Invoke(nextStep);
                    _lod.OnWeekElapsed(nextStep);
                }

                // 2) Roll for an interrupting life-sim event (Tier 1 active league only).
                EventRollContext context = _rollContextFactory(nextStep);
                GameEvent? rolled = _events.RollForDay(nextStep, context);
                if (rolled is null)
                    continue;

                if (rolled.Tier == EventTier.Low)
                {
                    // Low stakes: unavoidable passive modifier, applied inline; keep advancing.
                    _events.ResolveLowStakes(rolled);
                    continue;
                }

                // High/Medium stakes: pause the clock and hand control to the UI.
                SetSpeed(TimeSpeed.Paused);
                IsCalendarSimulating = false;
                var interruption = new TimeAdvanceResult(_clock.Current, Completed: false,
                    Interrupted: true, PendingEvent: rolled, ResumeToken: _activeResumeToken);
                AdvanceInterrupted?.Invoke(interruption);
                return interruption;
            }

            IsCalendarSimulating = false;
            SetSpeed(_speedBeforeCalendar);
            return new TimeAdvanceResult(_clock.Current, Completed: true, Interrupted: false,
                PendingEvent: null, ResumeToken: _activeResumeToken);
        }
        catch
        {
            IsCalendarSimulating = false;
            throw;
        }
    }
}
