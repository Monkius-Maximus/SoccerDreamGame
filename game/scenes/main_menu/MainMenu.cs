using Godot;
using SoccerDreamGame.Autoload;
using SoccerSim.Core.Domain;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// Entry scene and the hub of the separated-scene architecture. Routes the player
/// into the life simulation, a live match, or the calendar / background simulation.
/// </summary>
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        GD.Print("[MainMenu] Ready — separated-scene architecture entry point.");

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        center.AddChild(box);

        var title = new Label
        {
            Text = "Soccer Dream Game",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 32);
        box.AddChild(title);

        var playMatch = new Button
        {
            Text = "Play Next Fixture",
            CustomMinimumSize = new Vector2(260, 48),
        };
        playMatch.Pressed += OnPlayNextFixturePressed;
        box.AddChild(playMatch);

        var advance = new Button
        {
            Text = "Advance Calendar",
            CustomMinimumSize = new Vector2(260, 48),
        };
        advance.Pressed += OnAdvanceCalendarPressed;
        box.AddChild(advance);
    }

    // Hand the human club's next fixture to the mode manager, which validates it (fail-fast) and
    // switches into Match mode. All scene/mode changes go through GameModeManager — one way in.
    private void OnPlayNextFixturePressed()
    {
        Match? fixture = GameBootstrap.Instance.PeekNextHumanFixture();
        if (fixture is null)
        {
            GD.Print("[MainMenu] No unplayed fixture available for the human club.");
            return;
        }

        GameModeManager.Instance.EnterMatch(fixture);
    }

    private void OnAdvanceCalendarPressed() => GameModeManager.Instance.EnterCalendar();
}
