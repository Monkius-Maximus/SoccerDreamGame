using Godot;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Modes;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// The single authority over which <see cref="GameMode"/> is active, the scene that backs it,
/// and the orchestration-level time controls (pause / fast-forward) for the active mode.
///
/// <para>
/// The split mirrors the rest of the project: the legal-transition rules live in the
/// engine-agnostic <see cref="ModeStateMachine"/> (unit-tested headlessly); this thin Node only
/// drives Godot scene loading and the engine clock. It is an autoload with
/// <see cref="Node.ProcessModeEnum.Always"/> so it keeps running while the tree is paused.
/// </para>
/// </summary>
public partial class GameModeManager : Node
{
    public static GameModeManager Instance { get; private set; } = null!;

    // Each mode is backed by exactly one scene. Loading is the hub / main menu.
    private static readonly Dictionary<GameMode, string> ScenePaths = new()
    {
        [GameMode.Loading] = "res://scenes/main_menu/MainMenu.tscn",
        [GameMode.Calendar] = "res://scenes/calendar/CalendarScene.tscn",
        [GameMode.LifeSim] = "res://scenes/lifesim/LifeSimScene.tscn",
        [GameMode.Match] = "res://scenes/match/MatchScene.tscn",
    };

    private readonly ModeStateMachine _machine = new(GameMode.Loading);

    private float _fastForwardScale = 1.0f;

    /// <summary>The fixture chosen for the active Match mode, if any. Set by <see cref="EnterMatch"/>.</summary>
    public Match? ActiveFixture { get; private set; }

    public GameMode CurrentMode => _machine.CurrentMode;

    public override void _Ready()
    {
        Instance = this;
        // Keep handling time control while the SceneTree is paused (autoloads survive scene swaps).
        ProcessMode = ProcessModeEnum.Always;
        _machine.ModeChanged += (from, to) => GD.Print($"[GameMode] {from} -> {to}");
        GD.Print("[GameMode] Ready. Active mode: Loading (hub menu).");
    }

    public void EnterCalendar() => SwitchTo(GameMode.Calendar);

    public void EnterLifeSim() => SwitchTo(GameMode.LifeSim);

    /// <summary>Return to the hub menu (the Loading mode's scene).</summary>
    public void EnterLoading() => SwitchTo(GameMode.Loading);

    /// <summary>
    /// Enter a rendered match for a specific fixture. Throws (fail-fast) if the fixture is null,
    /// already played, or references clubs that do not exist — the Match Engine's entry contract.
    /// </summary>
    public void EnterMatch(Match fixture)
    {
        MatchEntryGuard.Validate(fixture, GameBootstrap.Instance.ClubExists);
        SwitchTo(GameMode.Match);
        ActiveFixture = fixture; // set after a successful transition; SwitchTo clears it otherwise.
        GD.Print($"[GameMode] Entered match {fixture.Id}: clubs {fixture.HomeTeamId} vs {fixture.AwayTeamId}.");
    }

    /// <summary>Pause the active mode. Resets TimeScale first to dodge the known pause-jitter edge case.</summary>
    public void Pause()
    {
        Engine.TimeScale = 1.0;
        GetTree().Paused = true;
        GD.Print("[GameMode] Paused.");
    }

    /// <summary>Resume the active mode, restoring any fast-forward scale that was in effect.</summary>
    public void Resume()
    {
        GetTree().Paused = false;
        Engine.TimeScale = _fastForwardScale;
        GD.Print($"[GameMode] Resumed at {_fastForwardScale}x.");
    }

    /// <summary>
    /// Fast-forward the active mode (e.g. 1x / 2x / 4x). Drives the engine clock, so it scales
    /// real-time scene playback and, through it, the in-game clock. Throws on a non-positive scale.
    /// </summary>
    public void SetFastForward(float scale)
    {
        if (scale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(scale), $"Fast-forward scale must be > 0, was {scale}.");

        _fastForwardScale = scale;
        if (!GetTree().Paused)
            Engine.TimeScale = scale;
        GD.Print($"[GameMode] Fast-forward set to {scale}x.");
    }

    private void SwitchTo(GameMode target)
    {
        _machine.TransitionTo(target); // throws on an illegal transition before we touch the tree
        ActiveFixture = null;
        GetTree().ChangeSceneToFile(ScenePaths[target]);
    }
}
