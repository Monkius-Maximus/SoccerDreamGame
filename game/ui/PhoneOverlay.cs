using Godot;
using SoccerSim.Core.Localization;

namespace SoccerDreamGame.Ui;

/// <summary>One application on the in-game phone.</summary>
/// <param name="Key">Stable identifier, also the root of its localisation keys.</param>
/// <param name="TitleKey">Localisation key for the app's name.</param>
/// <param name="BuildView">Builds the app's screen on demand, so a closed app costs nothing.</param>
public sealed record PhoneApp(string Key, string TitleKey, Func<Control> BuildView);

/// <summary>
/// The in-game phone: the quick menu, in the shape the design concept asked for.
///
/// <para>
/// The idea is borrowed from The Sims and it earns its place for a specific reason — it gives the
/// game one diegetic surface for everything that is <i>not</i> the world, so the human never leaves
/// the fiction to check their schedule. It also gives the interface somewhere to grow: a new system
/// becomes a new app, not another tab bolted onto a HUD.
/// </para>
///
/// <para>
/// Every app here is a <b>view onto a system that already exists</b>. That is the rule worth
/// keeping: the phone must never become the place where state secretly lives.
/// </para>
/// </summary>
public partial class PhoneOverlay : Control
{
    private readonly List<PhoneApp> _apps = [];

    private ILocalizer _text = null!;
    private PanelContainer? _shell;
    private VBoxContainer _body = null!;
    private Label _title = null!;
    private Button _back = null!;

    /// <summary>Raised whenever the phone opens or closes, so the prompt bar can re-label itself.</summary>
    public event Action<bool>? VisibilityToggled;

    /// <summary>True while an app is open rather than the home screen.</summary>
    public bool IsInsideApp { get; private set; }

    /// <summary>
    /// Attach the localizer and the app list. Safe to call again — a career-role switch re-configures
    /// so the apps bind to the replacement services, and rebuilding the shell each time would stack
    /// a second phone on top of the first.
    /// </summary>
    public void Configure(ILocalizer text, IEnumerable<PhoneApp> apps)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(apps);

        _text = text;
        _apps.Clear();
        _apps.AddRange(apps);

        if (_shell is null || !IsInstanceValid(_shell))
            BuildShell();

        ShowHome();
        Visible = false;
    }

    /// <summary>Open or close the phone.</summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
            ShowHome();
        VisibilityToggled?.Invoke(Visible);
    }

    /// <summary>Close the phone outright.</summary>
    public void Close()
    {
        if (!Visible)
            return;

        Visible = false;
        VisibilityToggled?.Invoke(false);
    }

    /// <summary>
    /// Handle a dismiss press. Returns true when the phone consumed it — inside an app it steps back
    /// to the home screen first, so one key does the expected thing at both depths.
    /// </summary>
    public bool HandleDismiss()
    {
        if (!Visible)
            return false;

        if (IsInsideApp)
        {
            ShowHome();
            return true;
        }

        Close();
        return true;
    }

    private void BuildShell()
    {
        Theme = UiTheme.Instance;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scrim);

        // Right-anchored and phone-shaped: it should read as a device the character is holding, not
        // as a modal dialog the game threw at them.
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_right", UiTokens.SpaceXl);
        margin.AddThemeConstantOverride("margin_top", UiTokens.HudBarHeight + UiTokens.SpaceXl);
        margin.AddThemeConstantOverride("margin_bottom", UiTokens.SpaceXl);
        AddChild(margin);

        // A full-rect container with an end-aligned row, rather than a RightWide preset: that preset
        // collapses to zero width and grows rightward off-screen, which is the kind of bug that only
        // appears once the thing is actually rendered.
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        margin.AddChild(row);

        _shell = new PanelContainer
        {
            CustomMinimumSize = new Vector2(380, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        row.AddChild(_shell);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiTokens.SpaceMd);
        _shell.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        column.AddChild(header);

        _back = new Button
        {
            Text = "←",
            CustomMinimumSize = new Vector2(UiTokens.MinTouchTarget, UiTokens.MinTouchTarget),
            Visible = false,
        };
        _back.Pressed += ShowHome;
        header.AddChild(_back);

        _title = new Label
        {
            ThemeTypeVariation = UiTheme.VariationPanelTitle,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(_title);

        column.AddChild(new HSeparator());

        _body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        column.AddChild(_body);
    }

    private void ShowHome()
    {
        IsInsideApp = false;
        _back.Visible = false;
        _title.Text = _text.Get(LocKeys.PhoneTitle);
        ClearBody();

        foreach (PhoneApp app in _apps)
        {
            var button = new Button
            {
                Text = _text.Get(app.TitleKey),
                CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
            };
            PhoneApp captured = app;
            button.Pressed += () => OpenApp(captured);
            _body.AddChild(button);
        }
    }

    private void OpenApp(PhoneApp app)
    {
        IsInsideApp = true;
        _back.Visible = true;
        _title.Text = _text.Get(app.TitleKey);
        ClearBody();
        _body.AddChild(app.BuildView());
    }

    private void ClearBody()
    {
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
    }
}
