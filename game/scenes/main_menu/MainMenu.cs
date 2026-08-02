using Godot;
using SoccerDreamGame.Autoload;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Localization;
using SoccerDreamGame.Ui;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// Entry scene and the hub of the separated-scene architecture. Routes the player
/// into the life simulation, a live match, or the calendar / background simulation.
/// </summary>
public partial class MainMenu : Control
{
    private Label _status = null!;

    public override void _Ready()
    {
        GD.Print("[MainMenu] Ready — separated-scene architecture entry point.");

        ILocalizer text = GameBootstrap.Instance.Text;
        Theme = UiTheme.Instance;

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", UiTokens.SpaceMd);
        center.AddChild(box);

        var title = new Label
        {
            Text = text.Get(LocKeys.MainTitle),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", UiTokens.FontDisplay);
        box.AddChild(title);

        // The life-sim is the mode this project has actually built out, and until now the hub had no
        // way into it — the two existing buttons went to a match and to the calendar.
        box.AddChild(Entry(text.Get(LocKeys.MainLifeSim), () => GameModeManager.Instance.EnterLifeSim()));
        box.AddChild(Entry(text.Get(LocKeys.MainPlayFixture), OnPlayNextFixturePressed));
        box.AddChild(Entry(text.Get(LocKeys.MainAdvanceCalendar), OnAdvanceCalendarPressed));

        _status = new Label
        {
            ThemeTypeVariation = UiTheme.VariationMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        box.AddChild(_status);
    }

    private static Button Entry(string label, Action onPressed)
    {
        var button = new Button
        {
            Text = label,
            CustomMinimumSize = new Vector2(280, UiTokens.MinTouchTarget),
        };
        button.Pressed += onPressed;
        return button;
    }

    // Hand the human club's next fixture to the mode manager, which validates it (fail-fast) and
    // switches into Match mode. All scene/mode changes go through GameModeManager — one way in.
    private void OnPlayNextFixturePressed()
    {
        Match? fixture = GameBootstrap.Instance.PeekNextHumanFixture();
        if (fixture is null)
        {
            // Say so on screen: a button that silently does nothing reads as a broken build.
            _status.Text = GameBootstrap.Instance.Text.Get(LocKeys.MainNoFixture);
            GD.Print("[MainMenu] No unplayed fixture available for the human club.");
            return;
        }

        GameModeManager.Instance.EnterMatch(fixture);
    }

    private void OnAdvanceCalendarPressed() => GameModeManager.Instance.EnterCalendar();
}
