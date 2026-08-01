using Godot;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;
using SoccerSim.Core.World;

namespace SoccerDreamGame.Ui;

/// <summary>
/// The off-pitch action bar: one button per <see cref="LifeActivity"/> the current career role may
/// perform <i>at the venue the human is standing in</i>.
///
/// <para>
/// This replaces the concept's fixed eight-icon quick-action row. Three things changed. The list is
/// generated from <see cref="LifeActivityCatalogue.AvailableAt"/> rather than hard-coded, so a
/// player career offers a gym session and a manager career offers film study without the scene
/// knowing either exists. It is gated by venue, so "Dormir" appears at home and "Fisioterapia" at
/// the medical department — which is what turns a menu of buttons into a reason to go somewhere.
/// And the entries that were never life activities (Match, Shop, Travel) are gone: entering a match
/// is a mode transition and travel is the map's job.
/// </para>
///
/// <para>
/// Every label is resolved through <see cref="ILocalizer"/>; the bar never holds a sentence.
/// </para>
/// </summary>
public partial class ActivityBar : PanelContainer
{
    private IWellbeingService? _service;
    private ILocalizer? _text;
    private WorldLocation? _location;
    private HBoxContainer? _buttons;
    private Label? _venueLabel;
    private Label? _emptyLabel;

    /// <summary>Raised after an activity resolves, so the host scene can log or animate it.</summary>
    public event Action<ActivityOutcome>? ActivityPerformed;

    /// <summary>The in-game date activities are stamped with. The host scene keeps this current.</summary>
    public DateTime CurrentDate { get; set; } = DateTime.MinValue;

    /// <summary>Attach to a career's wellbeing and build the buttons its role and venue allow.</summary>
    public void Bind(IWellbeingService service, ILocalizer text, WorldLocation location)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(location);

        _service = service;
        _text = text;
        _location = location;

        if (_buttons is null || !IsInstanceValid(_buttons))
            BuildRoot();

        Rebuild();
    }

    /// <summary>Re-filter for a new venue without re-binding the service.</summary>
    public void SetLocation(WorldLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        _location = location;
        if (_service is not null)
            Rebuild();
    }

    private void BuildRoot()
    {
        Theme = UiTheme.Instance;

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        AddChild(root);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        root.AddChild(header);

        header.AddChild(new Label
        {
            Text = _text!.Get(LocKeys.PanelActions),
            ThemeTypeVariation = UiTheme.VariationPanelTitle,
        });

        // Naming the venue is not decoration: it is the explanation for why this particular set of
        // buttons is on screen and another set is not.
        _venueLabel = new Label
        {
            ThemeTypeVariation = UiTheme.VariationMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(_venueLabel);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        _buttons = new HBoxContainer();
        _buttons.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        scroll.AddChild(_buttons);

        _emptyLabel = new Label
        {
            ThemeTypeVariation = UiTheme.VariationMuted,
            Visible = false,
        };
        root.AddChild(_emptyLabel);
    }

    private void Rebuild()
    {
        foreach (Node child in _buttons!.GetChildren())
        {
            _buttons.RemoveChild(child);
            child.QueueFree();
        }

        _venueLabel!.Text = _text!.Get(_location!.NameKey);

        IReadOnlyList<LifeActivity> available =
            LifeActivityCatalogue.AvailableAt(_service!.Role, _location);

        foreach (LifeActivity activity in available)
            _buttons.AddChild(BuildButton(activity));

        // A venue with nothing to do is a real state (standing in the stadium as a player), and
        // saying so beats an unexplained empty row.
        _emptyLabel!.Visible = available.Count == 0;
        if (available.Count == 0)
            _emptyLabel.Text = _text.Get(LocKeys.PanelNoChange);
    }

    private Button BuildButton(LifeActivity activity)
    {
        var button = new Button
        {
            Text = _text!.Get(activity.NameKey),
            TooltipText = DescribeTrade(activity),
            // Above the comfortable click-target floor: the concept's 32px-tall rows were the
            // single worst usability problem in it.
            CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
        };
        button.Pressed += () => Perform(activity);
        return button;
    }

    /// <summary>
    /// Spell out every need the activity moves, plus duration and cost. No activity in the catalogue
    /// is purely positive, and the player should not discover that by surprise.
    /// </summary>
    private string DescribeTrade(LifeActivity activity)
    {
        IEnumerable<string> deltas = activity.NeedDeltas.Select(delta =>
            $"{(delta.Delta >= 0 ? "+" : "")}{delta.Delta:0} {_text!.Get(LocKeys.NeedShort(delta.Need))}");

        string trade = string.Join("   ", deltas);
        string description = _text!.Get(activity.DescriptionKey);
        string duration = $"{_text.Get(LocKeys.ActivityDuration)}: {activity.DurationHours:0.#}h";
        string cost = activity.Cost > 0
            ? $"\n{_text.Get(LocKeys.ActivityCost)}: {activity.Cost:N0}"
            : string.Empty;

        return $"{description}\n{duration}\n{trade}{cost}";
    }

    private void Perform(LifeActivity activity)
    {
        if (_service is null)
            return;

        ActivityOutcome outcome = _service.Perform(activity.Key, CurrentDate);
        ActivityPerformed?.Invoke(outcome);
    }
}
