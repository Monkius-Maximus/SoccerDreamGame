using Godot;
using SoccerDreamGame.Ui;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;
using SoccerSim.Core.Modes;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// Hosts the persistent <see cref="HudBar"/> on its own <see cref="CanvasLayer"/> so the strip
/// survives every <c>ChangeSceneToFile</c>.
///
/// <para>
/// This is the structural half of the idea the UI concept got right: the strip has to outlive the
/// scenes, otherwise the human loses the simulation's state at every mode switch — which is exactly
/// when they most need it. Hiding it in the hub menu keeps the main menu clean, since there is no
/// live simulation to report there.
/// </para>
///
/// <para>
/// Tiles are only registered for state the core can actually answer today. League position, balance
/// and season goals belong here too, but each needs a read model that does not exist yet, and an
/// always-blank tile is worse than an absent one.
/// </para>
/// </summary>
public partial class HudNode : Node
{
    /// <summary>Below the event modal's overlay, above ordinary scene content.</summary>
    private const int HudLayer = 50;

    public static HudNode Instance { get; private set; } = null!;

    private HudBar _bar = null!;
    private IWellbeingService _wellbeing = null!;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        var layer = new CanvasLayer { Layer = HudLayer };
        AddChild(layer);

        // Anchor the strip to the top edge and let it stretch across whatever resolution is running.
        var anchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        anchor.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        layer.AddChild(anchor);

        _bar = new HudBar();
        _bar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        anchor.AddChild(_bar);

        ILocalizer text = GameBootstrap.Instance.Text;
        _bar.UseLocalizer(text);
        _bar.AddTile(HudBar.TileDate, text.Get(LocKeys.HudDate));
        _bar.AddTile(HudBar.TileRole, text.Get(LocKeys.HudCareer));
        _bar.AddTile(HudBar.TileWellbeing, text.Get(LocKeys.HudWellbeing));
        _bar.AddTile(HudBar.TileForm, text.Get(LocKeys.HudForm));

        _wellbeing = GameBootstrap.Instance.Wellbeing;
        _wellbeing.Changed += OnWellbeingChanged;
        GameModeManager.Instance.ModeChanged += OnModeChanged;

        _bar.SetTile(HudBar.TileRole,
            text.Get(LocKeys.Role(_wellbeing.Role)).ToUpperInvariant(),
            UiTokens.Positive);
        _bar.ApplyWellbeing(_wellbeing.Snapshot);
        ApplyVisibility(GameModeManager.Instance.CurrentMode);
    }

    public override void _ExitTree()
    {
        if (_wellbeing is not null)
            _wellbeing.Changed -= OnWellbeingChanged;
        if (GameModeManager.Instance is not null)
            GameModeManager.Instance.ModeChanged -= OnModeChanged;
    }

    public override void _Process(double delta) =>
        _bar.SetTile(HudBar.TileDate, GameBootstrap.Instance.Time.CurrentDate.ToString("dd MMM yyyy"));

    private void OnWellbeingChanged(WellbeingSnapshot snapshot) => _bar.ApplyWellbeing(snapshot);

    private void OnModeChanged(GameMode previous, GameMode next) => ApplyVisibility(next);

    /// <summary>The hub menu has no live simulation to report, so the strip stays out of its way.</summary>
    private void ApplyVisibility(GameMode mode) => _bar.Visible = mode != GameMode.Loading;
}
