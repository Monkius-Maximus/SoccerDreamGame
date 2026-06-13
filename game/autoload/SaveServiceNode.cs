using Godot;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// Resolves the save-file location and is the future home of explicit save/load
/// orchestration. The database itself is opened by <see cref="GameBootstrap"/>.
/// </summary>
public partial class SaveServiceNode : Node
{
    public string DatabasePath => ProjectSettings.GlobalizePath("user://save.db");

    public override void _Ready() => GD.Print("[SaveService] Save database path: ", DatabasePath);
}
