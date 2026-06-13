using Godot;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// The Life Simulation scene: isometric projection with grid-based housing, routing,
/// and object interactions (GDD §1). Daily tasks here feed the economy/progression loop.
/// </summary>
public partial class LifeSimScene : Node2D
{
    public override void _Ready() =>
        GD.Print("[LifeSimScene] Isometric life-sim loaded.");
}
