using Godot;

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
    }

    private void OnPlayNextFixturePressed() =>
        GetTree().ChangeSceneToFile("res://scenes/match/MatchScene.tscn");
}
