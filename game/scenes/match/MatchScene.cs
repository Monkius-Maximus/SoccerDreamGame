using Godot;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// The Match Engine scene: side-on orthographic view, horizontal scrolling, 16-bit
/// physical constraints and sprite logic (GDD §1). It reads each player's EFFECTIVE
/// attributes (base + form, unless in arcade mode) from the core simulation.
/// </summary>
public partial class MatchScene : Node2D
{
    public override void _Ready() =>
        GD.Print("[MatchScene] Side-on match engine loaded.");
}
