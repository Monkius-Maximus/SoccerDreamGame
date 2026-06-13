using Godot;
using SoccerSim.Core.Time;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// The single Godot coupling for time. Forwards the engine's per-frame delta into the
/// headless <see cref="ITimeManager"/> so Pause / 2x / 4x are driven by real frame time
/// while all the actual time logic stays engine-agnostic and unit-testable.
/// </summary>
public partial class TimeManagerNode : Node
{
    private ITimeManager? _time;

    public override void _Ready() => _time = GameBootstrap.Instance.Time;

    public override void _Process(double delta) => _time?.Tick(delta);

    public void SetSpeed(TimeSpeed speed) => _time?.SetSpeed(speed);
}
