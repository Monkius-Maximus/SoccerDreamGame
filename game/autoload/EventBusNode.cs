using Godot;
using SoccerDreamGame.Ui;
using SoccerSim.Core.Events;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// Bridges the core <see cref="IEventManager"/> to Godot scenes. When the calendar loop raises a
/// High/Medium event, this presents the resolution modal and returns the human's chosen outcome so
/// the simulation can resume.
///
/// <para>
/// The modal is hosted on this autoload's own <see cref="CanvasLayer"/> rather than inside the
/// active scene: an interrupt can fire during a scene change, and a dialog parented to a scene that
/// is being freed would strand the resume token and stall the calendar forever.
/// </para>
/// </summary>
public partial class EventBusNode : Node
{
    /// <summary>Above ordinary scene content, below nothing else the game draws.</summary>
    private const int OverlayLayer = 100;

    private CanvasLayer _overlay = null!;

    public override void _Ready()
    {
        // Survive the SceneTree pause: the calendar pauses itself when it interrupts, and a modal
        // that stops processing at that moment could never be answered.
        ProcessMode = ProcessModeEnum.Always;

        _overlay = new CanvasLayer { Layer = OverlayLayer };
        AddChild(_overlay);

        GameBootstrap.Instance.Events.ResolutionRequested += OnResolutionRequestedAsync;
    }

    public override void _ExitTree()
    {
        if (GameBootstrap.Instance is not null)
            GameBootstrap.Instance.Events.ResolutionRequested -= OnResolutionRequestedAsync;
    }

    private Task<EventResolutionResult> OnResolutionRequestedAsync(EventResolutionRequest request)
    {
        EventDefinition definition = GameBootstrap.Instance.GetEventDefinition(request.Event.DefinitionKey);
        IReadOnlyDictionary<string, int> traitWeights =
            GameBootstrap.Instance.Career?.TraitWeights ?? new Dictionary<string, int>();

        GD.Print($"[EventBus] Resolving {request.Event.Tier} event '{request.Event.DefinitionKey}'.");

        var dialog = new EventResolutionDialog();
        _overlay.AddChild(dialog);
        dialog.ProcessMode = Node.ProcessModeEnum.Always;
        return dialog.Present(request, definition, traitWeights, GameBootstrap.Instance.Text);
    }
}
