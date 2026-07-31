using Godot;
using SoccerSim.Core.LifeSim;

namespace SoccerDreamGame.Ui;

/// <summary>
/// The off-pitch action bar: one button per <see cref="LifeActivity"/> the current career role may
/// perform.
///
/// <para>
/// This replaces the concept's fixed eight-icon quick-action row. Two things changed. The list is
/// no longer hard-coded — it is generated from <see cref="LifeActivityCatalogue.For"/>, so a player
/// career offers a gym session and a manager career offers film study without the scene knowing
/// either exists. And the entries that were never life activities in the first place (Match, Shop,
/// Travel) are gone: entering a match is a mode transition and shopping is an economy screen, so
/// dressing them as need actions only blurred what the bar does.
/// </para>
///
/// <para>
/// Each button's tooltip states the full trade, including the costs. No activity in the catalogue
/// is purely positive, and the UI should not let the player discover that by surprise.
/// </para>
/// </summary>
public partial class ActivityBar : PanelContainer
{
    private IWellbeingService? _service;
    private HBoxContainer? _buttons;

    /// <summary>Raised after an activity resolves, so the host scene can log or animate it.</summary>
    public event Action<ActivityOutcome>? ActivityPerformed;

    /// <summary>The in-game date activities are stamped with. The host scene keeps this current.</summary>
    public DateTime CurrentDate { get; set; } = DateTime.MinValue;

    /// <summary>Attach to a career's wellbeing and build the buttons its role allows.</summary>
    public void Bind(IWellbeingService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        if (_buttons is null || !IsInstanceValid(_buttons))
            _buttons = BuildRoot();

        foreach (Node child in _buttons.GetChildren())
            child.QueueFree();

        foreach (LifeActivity activity in LifeActivityCatalogue.For(service.Role))
            _buttons.AddChild(BuildButton(activity));
    }

    /// <summary>Builds the panel chrome once and returns the container the buttons live in.</summary>
    private HBoxContainer BuildRoot()
    {
        Theme = UiTheme.Instance;

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        AddChild(root);

        root.AddChild(new Label { Text = "ACTIONS", ThemeTypeVariation = UiTheme.VariationPanelTitle });

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        scroll.AddChild(buttons);
        return buttons;
    }

    private Button BuildButton(LifeActivity activity)
    {
        var button = new Button
        {
            Text = activity.DisplayName,
            TooltipText = DescribeTrade(activity),
            // Above the comfortable click-target floor: the concept's 32px-tall rows were the
            // single worst usability problem in it.
            CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
        };
        button.Pressed += () => Perform(activity);
        return button;
    }

    /// <summary>Spell out every need the activity moves, plus its duration and cost.</summary>
    private static string DescribeTrade(LifeActivity activity)
    {
        IEnumerable<string> deltas = activity.NeedDeltas
            .Select(delta => $"{(delta.Delta >= 0 ? "+" : "")}{delta.Delta:0} {delta.Need}");

        string trade = string.Join("   ", deltas);
        string cost = activity.Cost > 0 ? $"\nCost: {activity.Cost:N0}" : string.Empty;
        return $"{activity.DisplayName} — {activity.DurationHours:0.#}h\n{trade}{cost}";
    }

    private void Perform(LifeActivity activity)
    {
        if (_service is null)
            return;

        ActivityOutcome outcome = _service.Perform(activity.Key, CurrentDate);
        ActivityPerformed?.Invoke(outcome);
    }
}
