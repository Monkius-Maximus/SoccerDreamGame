using Godot;
using SoccerDreamGame.Ui;
using SoccerDreamGame.Ui.Input;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;
using SoccerSim.Core.World;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// Owns the parts of the interface that belong to the game rather than to any one scene: the input
/// map, the device-aware prompt strip, and the in-game phone.
///
/// <para>
/// All three are here for the same reason the HUD strip is — they must outlive
/// <c>ChangeSceneToFile</c>. A phone you cannot open during a match, or button hints that vanish
/// when the scene changes, would be worse than not having them.
/// </para>
/// </summary>
public partial class InterfaceNode : Node
{
    /// <summary>Above the HUD strip, below the event modal — an interrupt still owns the screen.</summary>
    private const int InterfaceLayer = 70;

    public static InterfaceNode Instance { get; private set; } = null!;

    private PhoneOverlay _phone = null!;
    private QuickMenuOverlay _menu = null!;
    private InputPromptBar _prompts = null!;
    private ILocalizer _text = null!;
    private InputDevice _device = InputDevice.Keyboard;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        GameInput.RegisterActions();
        _text = GameBootstrap.Instance.Text;

        var layer = new CanvasLayer { Layer = InterfaceLayer };
        AddChild(layer);

        var promptAnchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        promptAnchor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        layer.AddChild(promptAnchor);

        _prompts = new InputPromptBar();
        _prompts.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        promptAnchor.AddChild(_prompts);

        _phone = new PhoneOverlay();
        layer.AddChild(_phone);
        _phone.Configure(_text, BuildApps());
        _phone.VisibilityToggled += _ => RefreshPrompts();

        // The quick menu is added after the phone, so it draws on top when both could be open.
        _menu = new QuickMenuOverlay();
        layer.AddChild(_menu);
        _menu.Configure(_text);
        _menu.VisibilityToggled += _ => RefreshPrompts();
        _menu.RoleSwitchRequested += OnRoleSwitchRequested;
        _menu.ShowRole(GameBootstrap.Instance.Wellbeing.Role);

        RefreshPrompts();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        DetectDevice(@event);

        if (@event.IsActionPressed(GameInput.ActionPhone))
        {
            _phone.Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!@event.IsActionPressed(GameInput.ActionMenu))
            return;

        // Esc / Start walks one step out at a time: inside a phone app -> the phone's home screen;
        // phone open -> closed; menu open -> closed; nothing open -> open the menu. One key, and it
        // always does the least surprising thing.
        if (_phone.HandleDismiss() || _menu.HandleDismiss())
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        _menu.Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Switch career and re-bind everything that held the old service. The gauges are carried over
    /// untouched — see <c>GameBootstrap.SwitchCareerRole</c>.
    /// </summary>
    private void OnRoleSwitchRequested(CareerRole role)
    {
        GameBootstrap.Instance.SwitchCareerRole(role);
        _menu.ShowRole(GameBootstrap.Instance.Wellbeing.Role);
        // Rebuild the apps so the health view binds to the replacement service rather than the
        // orphaned one.
        _phone.Configure(_text, BuildApps());
    }

    /// <summary>
    /// Swap the glyph family the moment the human touches a different device — the behaviour that
    /// makes prompts trustworthy rather than a guess made at startup.
    /// </summary>
    private void DetectDevice(InputEvent @event)
    {
        InputDevice detected = @event switch
        {
            InputEventKey or InputEventMouse => InputDevice.Keyboard,
            InputEventJoypadButton or InputEventJoypadMotion => DetectPad(),
            _ => _device,
        };

        if (detected == _device)
            return;

        _device = detected;
        _prompts.SetDevice(_device);
    }

    /// <summary>
    /// Guess the pad family from its reported name. Godot does not expose a vendor enum, and the
    /// distinction only changes which glyphs are drawn, so a name sniff is proportionate.
    /// </summary>
    private static InputDevice DetectPad()
    {
        foreach (int deviceId in Godot.Input.GetConnectedJoypads())
        {
            string name = Godot.Input.GetJoyName(deviceId).ToLowerInvariant();
            if (name.Contains("ps") || name.Contains("dualshock") || name.Contains("dualsense")
                || name.Contains("playstation"))
            {
                return InputDevice.PlayStation;
            }
        }

        return InputDevice.Xbox;
    }

    private void RefreshPrompts()
    {
        // Prompts describe what the verbs mean HERE. The phone re-labels Menu from "quick menu" to
        // "back"/"close" because that is what it does while the phone is up.
        IReadOnlyList<InputPrompt> prompts = _phone.Visible
            ?
            [
                new InputPrompt(InputVerb.Confirm, LocKeys.PromptSelect),
                new InputPrompt(InputVerb.Menu, _phone.IsInsideApp ? LocKeys.PromptBack : LocKeys.PromptClose),
            ]
            :
            [
                new InputPrompt(InputVerb.Confirm, LocKeys.PromptSelect),
                new InputPrompt(InputVerb.Phone, LocKeys.PromptPhone),
                new InputPrompt(InputVerb.Menu, LocKeys.PromptMenu),
            ];

        _prompts.Show(_text, prompts);
        _prompts.SetDevice(_device);
    }

    /// <summary>
    /// The phone's apps. Each is a VIEW onto a system that already exists — the phone must never
    /// become the place where state secretly lives.
    /// </summary>
    private IReadOnlyList<PhoneApp> BuildApps() =>
    [
        new PhoneApp("health", LocKeys.PhoneAppHealth, BuildHealthApp),
        new PhoneApp("agenda", LocKeys.PhoneAppAgenda, BuildAgendaApp),
        new PhoneApp("map", LocKeys.PhoneAppMap, BuildMapApp),
        new PhoneApp("bank", LocKeys.PhoneAppBank, BuildBankApp),
    ];

    private Control BuildBankApp()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiTokens.SpaceSm);

        GameBootstrap game = GameBootstrap.Instance;
        int walletId = game.Career?.HumanPlayerId ?? 1;

        // Reads the live PlayerFinances balance — the same one an activity's cost is debited from,
        // so what this screen shows and what the action bar can afford cannot disagree.
        column.AddChild(Row(
            _text.Get(LocKeys.BankBalance),
            game.Economy.GetBalance(walletId).ToString("N0")));

        return column;
    }

    private Control BuildHealthApp()
    {
        // The very same panel the life-sim scene uses, bound to the very same service.
        var panel = new NeedsPanel();
        panel.Bind(GameBootstrap.Instance.Wellbeing, _text);
        return panel;
    }

    private Control BuildAgendaApp()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiTokens.SpaceSm);

        GameBootstrap game = GameBootstrap.Instance;
        column.AddChild(Row(_text.Get(LocKeys.HudDate), game.Time.CurrentDate.ToString("dd MMM yyyy")));
        column.AddChild(Row(_text.Get(LocKeys.HudCareer), _text.Get(LocKeys.Role(game.Wellbeing.Role))));
        column.AddChild(Row(
            _text.Get(LocKeys.PhoneCurrentLocation),
            _text.Get(game.CurrentLocation.NameKey)));

        return column;
    }

    private Control BuildMapApp()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiTokens.SpaceXs);

        GameBootstrap game = GameBootstrap.Instance;
        foreach (WorldLocation venue in game.World.All.Where(location => location.IsVenue))
        {
            bool here = venue.Id == game.CurrentLocation.Id;
            var button = new Button
            {
                Text = here
                    ? $"• {_text.Get(venue.NameKey)}"
                    : $"{_text.Get(venue.NameKey)}  ({venue.TravelMinutes:0} {_text.Get(LocKeys.PhoneTravelTime)})",
                TooltipText = _text.Get(venue.DescriptionKey),
                Disabled = here,
                CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
            };

            string id = venue.Id;
            button.Pressed += () =>
            {
                game.TravelTo(id);
                _phone.Close();
            };
            column.AddChild(button);
        }

        return column;
    }

    private static Control Row(string label, string value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        row.AddChild(new Label
        {
            Text = label,
            ThemeTypeVariation = UiTheme.VariationMuted,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        row.AddChild(new Label { Text = value });
        return row;
    }
}
