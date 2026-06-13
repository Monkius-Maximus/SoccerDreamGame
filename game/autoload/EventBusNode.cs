using Godot;
using SoccerSim.Core.Events;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// Bridges the core <see cref="IEventManager"/> to Godot scenes. When the calendar
/// loop raises a High/Medium event, this presents the matching scene and returns the
/// player's chosen outcome so the simulation can resume.
/// </summary>
public partial class EventBusNode : Node
{
    public override void _Ready() =>
        GameBootstrap.Instance.Events.ResolutionRequested += OnResolutionRequestedAsync;

    public override void _ExitTree()
    {
        if (GameBootstrap.Instance is not null)
            GameBootstrap.Instance.Events.ResolutionRequested -= OnResolutionRequestedAsync;
    }

    private Task<EventResolutionResult> OnResolutionRequestedAsync(EventResolutionRequest request)
    {
        // TODO: instance and await the appropriate scene, then return its outcome:
        //   High   -> res://scenes/events/HighStakesMiniGame.tscn (mini-game)
        //   Medium -> res://scenes/events/MediumStakesDialogue.tscn (trait-gated choices)
        GD.Print($"[EventBus] Resolving {request.Event.Tier} event '{request.Event.DefinitionKey}'.");

        var noOp = new EventResolutionResult(
            request.Event.Id,
            Array.Empty<StatDelta>(),
            Array.Empty<ResourceDelta>());
        return Task.FromResult(noOp);
    }
}
