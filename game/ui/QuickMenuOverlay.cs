using Godot;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;

namespace SoccerDreamGame.Ui;

/// <summary>
/// The quick menu — what <c>Esc</c> / <c>Start</c> opens.
///
/// <para>
/// Distinct from the phone on purpose. The phone is diegetic and holds what the <i>character</i>
/// can see; this is the system menu and holds what the <i>player</i> controls. Blurring the two is
/// how a Sims-style phone ends up with a "Quit to Desktop" button in it.
/// </para>
///
/// <para>
/// Its first real entry is the career-role switch, which is also the cheapest way to make the
/// manager career reachable: before this, flipping to it meant editing the database by hand.
/// </para>
/// </summary>
public partial class QuickMenuOverlay : Control
{
    private ILocalizer _text = null!;
    private VBoxContainer _body = null!;
    private Label _roleValue = null!;

    /// <summary>Raised when the menu opens or closes, so the prompt strip can re-label itself.</summary>
    public event Action<bool>? VisibilityToggled;

    /// <summary>Asked to switch career role. The host performs it; this control only offers it.</summary>
    public event Action<CareerRole>? RoleSwitchRequested;

    public void Configure(ILocalizer text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        BuildShell();
        Visible = false;
    }

    public void Toggle()
    {
        Visible = !Visible;
        VisibilityToggled?.Invoke(Visible);
    }

    /// <summary>Close if open. Returns true when it consumed the dismiss.</summary>
    public bool HandleDismiss()
    {
        if (!Visible)
            return false;

        Visible = false;
        VisibilityToggled?.Invoke(false);
        return true;
    }

    /// <summary>Refresh the displayed role after a switch actually lands.</summary>
    public void ShowRole(CareerRole role) => _roleValue.Text = _text.Get(LocKeys.Role(role));

    private void BuildShell()
    {
        Theme = UiTheme.Instance;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scrim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var card = new PanelContainer { CustomMinimumSize = new Vector2(440, 0) };
        centre.AddChild(card);

        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", UiTokens.SpaceMd);
        card.AddChild(_body);

        _body.AddChild(new Label
        {
            Text = _text.Get(LocKeys.MenuTitle),
            ThemeTypeVariation = UiTheme.VariationPanelTitle,
        });
        _body.AddChild(new HSeparator());

        // ── Career role ─────────────────────────────────────────────────────────────────
        var roleRow = new HBoxContainer();
        roleRow.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        _body.AddChild(roleRow);

        roleRow.AddChild(new Label
        {
            Text = _text.Get(LocKeys.MenuCareerRole),
            ThemeTypeVariation = UiTheme.VariationMuted,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _roleValue = new Label { VerticalAlignment = VerticalAlignment.Center };
        roleRow.AddChild(_roleValue);

        var roleButtons = new HBoxContainer();
        roleButtons.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        _body.AddChild(roleButtons);

        foreach (CareerRole role in Enum.GetValues<CareerRole>())
        {
            var button = new Button
            {
                Text = _text.Get(LocKeys.Role(role)),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
            };
            CareerRole captured = role;
            button.Pressed += () => RoleSwitchRequested?.Invoke(captured);
            roleButtons.AddChild(button);
        }

        // Says out loud what the switch does, because "nothing was reset" is surprising otherwise.
        _body.AddChild(new Label
        {
            Text = _text.Get(LocKeys.MenuRoleHint),
            ThemeTypeVariation = UiTheme.VariationMuted,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _body.AddChild(new HSeparator());

        var resume = new Button
        {
            Text = _text.Get(LocKeys.MenuResume),
            CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget),
        };
        resume.Pressed += () => HandleDismiss();
        _body.AddChild(resume);
    }
}
