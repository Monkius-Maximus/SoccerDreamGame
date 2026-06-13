using Godot;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// Entry scene and the hub of the separated-scene architecture. Routes the player
/// into the life simulation, a live match, or the calendar / background simulation.
/// </summary>
public partial class MainMenu : Control
{
    public override void _Ready() =>
        GD.Print("[MainMenu] Ready — separated-scene architecture entry point.");
}
