using Godot;
using SoccerDreamGame.Autoload;
using SoccerSim.Core.Time;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// Rendered calendar. Kicks off a background calendar simulation and handles the
/// EventTrigger interruption when an event fires: it surfaces the event, then resumes
/// advancing toward the original target date.
/// </summary>
public partial class CalendarScene : Control
{
    /// <summary>Advance the simulated calendar to <paramref name="target"/>, handling interrupts.</summary>
    public void AdvanceTo(DateTime target)
    {
        ITimeManager time = GameBootstrap.Instance.Time;
        TimeAdvanceResult result = time.AdvanceCalendar(target);

        while (result is { Interrupted: true, PendingEvent: not null })
        {
            GD.Print($"[Calendar] Interrupted by {result.PendingEvent.Tier} event on {result.ReachedDate:d}.");

            // A full implementation awaits EventBus to resolve High/Medium events here;
            // the scaffold simply resumes to keep the loop demonstrable.
            result = time.ResumeCalendar(result.ResumeToken, target);
        }

        GD.Print($"[Calendar] Simulation reached {result.ReachedDate:d}.");
    }
}
