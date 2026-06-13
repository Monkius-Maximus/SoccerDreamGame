namespace SoccerSim.Core.Events;

/// <summary>
/// Rolls, classifies, and resolves life-sim events. Pure with respect to the
/// engine: High/Medium events surface via <see cref="RequestResolutionAsync"/>,
/// which the Godot layer fulfils by presenting a mini-game or dialogue scene.
/// </summary>
public interface IEventManager
{
    /// <summary>All registered event templates, ordered High → Low.</summary>
    IReadOnlyList<EventDefinition> Definitions { get; }

    /// <summary>
    /// Roll every definition (High → Low) against its trait-weighted probability
    /// for the given day. Returns the first event that fires, or null.
    /// </summary>
    GameEvent? RollForDay(DateTime date, EventRollContext context);

    /// <summary>Resolve a Low-stakes event immediately (no UI); returns the applied deltas.</summary>
    EventResolutionResult ResolveLowStakes(GameEvent gameEvent);

    /// <summary>
    /// Ask the presentation layer to resolve a High/Medium event. Throws if no
    /// handler is registered. Used by the calendar interrupt/resume path.
    /// </summary>
    Task<EventResolutionResult> RequestResolutionAsync(GameEvent gameEvent, Guid resumeToken);

    /// <summary>Fulfilled by the Godot layer to present a High/Medium resolution scene.</summary>
    event Func<EventResolutionRequest, Task<EventResolutionResult>>? ResolutionRequested;
}
