using Godot;
using SoccerSim.Core.Events;
using SoccerSim.Core.Localization;

namespace SoccerDreamGame.Ui;

/// <summary>
/// The modal that resolves a High/Medium event and hands the outcome back to the calendar.
///
/// <para>
/// This screen did not exist anywhere in the UI concept, despite the interrupt/resume cycle being
/// the spine of the game's architecture: <c>TimeManager.AdvanceCalendar</c> freezes the clock, emits
/// a resume token, and waits for the presentation layer to decide what happened. Without this
/// dialog the token has nowhere to go.
/// </para>
///
/// <para>
/// Awaiting UI here is safe: <c>RequestResolutionAsync</c> is invoked by the Godot layer AFTER the
/// advance has already returned its interruption, never from inside the simulation loop, so nothing
/// re-enters the core while the modal is open.
/// </para>
///
/// <para>
/// The dialog is deliberately dismissal-proof — no scrim click, no close button. An unresolved
/// event would strand the resume token and silently stall the calendar.
/// </para>
/// </summary>
public partial class EventResolutionDialog : Control
{
    private TaskCompletionSource<EventResolutionResult>? _completion;
    private GameEvent? _event;
    private ILocalizer _text = null!;
    private string _definitionKey = string.Empty;

    /// <summary>
    /// Build and show the modal, returning the task the event bus awaits. Choices the human's traits
    /// do not unlock are filtered out before rendering.
    /// </summary>
    public Task<EventResolutionResult> Present(
        EventResolutionRequest request,
        EventDefinition definition,
        IReadOnlyDictionary<string, int> traitWeights,
        ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(traitWeights);
        ArgumentNullException.ThrowIfNull(text);

        _event = request.Event;
        _text = text;
        _definitionKey = definition.Key;
        _completion = new TaskCompletionSource<EventResolutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Theme = UiTheme.Instance;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow clicks so the scene behind cannot be used

        BuildScrim();
        BuildCard(definition, traitWeights);

        return _completion.Task;
    }

    private void BuildScrim()
    {
        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        scrim.MouseFilter = MouseFilterEnum.Stop;
        AddChild(scrim);
    }

    private void BuildCard(EventDefinition definition, IReadOnlyDictionary<string, int> traitWeights)
    {
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var card = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        centre.AddChild(card);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", UiTokens.SpaceMd);
        card.AddChild(body);

        // Tier is the honest headline: it tells the human whether this is a formality or a decision
        // that will follow them.
        body.AddChild(new Label
        {
            Text = _text.Get(LocKeys.EventTier(definition.Tier)),
            ThemeTypeVariation = definition.Tier == EventTier.High
                ? UiTheme.VariationDanger
                : UiTheme.VariationMuted,
        });

        body.AddChild(new Label
        {
            Text = _text.Get(definition.TitleKey),
            ThemeTypeVariation = UiTheme.VariationPanelTitle,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        if (_text.Has(definition.PromptKey))
        {
            body.AddChild(new Label
            {
                Text = _text.Get(definition.PromptKey),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }

        body.AddChild(new HSeparator());

        IReadOnlyList<EventChoice> available = definition.Choices
            .Where(choice => choice.IsAvailableTo(traitWeights))
            .ToArray();

        if (available.Count == 0)
        {
            // A not-yet-authored event must still be dismissible, or the calendar stalls forever.
            var acknowledge = new Button
            {
                Text = _text.Get(LocKeys.EventAcknowledge),
                CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
            };
            acknowledge.Pressed += () => Resolve(
                Array.Empty<StatDelta>(), Array.Empty<ResourceDelta>());
            body.AddChild(acknowledge);
            return;
        }

        foreach (EventChoice choice in available)
            body.AddChild(BuildChoice(choice));
    }

    private Control BuildChoice(EventChoice choice)
    {
        var wrapper = new VBoxContainer();
        wrapper.AddThemeConstantOverride("separation", UiTokens.SpaceXs);

        var button = new Button
        {
            Text = _text.Get(choice.LabelKey(_definitionKey)),
            CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
            TooltipText = DescribeConsequences(choice),
        };
        button.Pressed += () => Resolve(choice.StatDeltas, choice.ResourceDeltas);
        wrapper.AddChild(button);

        // Has() rather than Get(): an unauthored description must stay absent, not render as a raw
        // "event.x.choice.y.desc" key underneath the button.
        if (_text.Has(choice.DescriptionKey(_definitionKey)))
        {
            wrapper.AddChild(new Label
            {
                Text = _text.Get(choice.DescriptionKey(_definitionKey)),
                ThemeTypeVariation = UiTheme.VariationMuted,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }

        return wrapper;
    }

    private string DescribeConsequences(EventChoice choice)
    {
        IEnumerable<string> stats = choice.StatDeltas
            .Select(delta => $"{(delta.Delta >= 0 ? "+" : "")}{delta.Delta} {delta.StatKey}");
        IEnumerable<string> resources = choice.ResourceDeltas
            .Select(delta => $"{(delta.Delta >= 0 ? "+" : "")}{delta.Delta:N0} {delta.ResourceKey}");

        string[] all = stats.Concat(resources).ToArray();
        return all.Length == 0 ? _text.Get(LocKeys.EventNoConsequence) : string.Join("   ", all);
    }

    private void Resolve(IReadOnlyList<StatDelta> stats, IReadOnlyList<ResourceDelta> resources)
    {
        // TrySetResult, not SetResult: a double-click on two buttons in the same frame must not
        // crash the resume path.
        _completion?.TrySetResult(new EventResolutionResult(_event!.Id, stats, resources));
        QueueFree();
    }
}
