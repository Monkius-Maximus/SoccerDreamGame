using Godot;
using SoccerDreamGame.Autoload;
using SoccerDreamGame.Ui;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;
using SoccerSim.Core.World;

namespace SoccerDreamGame.Scenes;

/// <summary>
/// The Life Simulation scene: isometric projection with grid-based housing, routing, and object
/// interactions (GDD §1). Daily tasks here feed the economy/progression loop. The
/// <see cref="Node2D"/> root is where that isometric world will be drawn; this class currently
/// builds the interface layer over it.
///
/// <para>
/// The overlay is the needs panel and the action bar, both bound to the live
/// <see cref="IWellbeingService"/>. Because the panels read the career's role from the service, this
/// one scene serves a player career and a manager career — the action bar simply offers a different
/// set of activities.
/// </para>
/// </summary>
public partial class LifeSimScene : Node2D
{
    /// <summary>Below the HUD strip so the two never fight for the same pixels.</summary>
    private const int OverlayLayer = 10;

    private NeedsPanel _needs = null!;
    private ActivityBar _activities = null!;
    private Label _log = null!;
    private ILocalizer _text = null!;

    public override void _Ready()
    {
        IWellbeingService wellbeing = GameBootstrap.Instance.Wellbeing;
        _text = GameBootstrap.Instance.Text;

        var layer = new CanvasLayer { Layer = OverlayLayer };
        AddChild(layer);

        var root = new MarginContainer();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // Clear the persistent HUD strip so the overlay starts below it.
        root.AddThemeConstantOverride("margin_top", UiTokens.HudBarHeight + UiTokens.SpaceMd);
        root.AddThemeConstantOverride("margin_left", UiTokens.SpaceMd);
        root.AddThemeConstantOverride("margin_right", UiTokens.SpaceMd);
        root.AddThemeConstantOverride("margin_bottom", UiTokens.SpaceMd);
        layer.AddChild(root);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiTokens.SpaceMd);
        root.AddChild(column);

        _needs = new NeedsPanel { CustomMinimumSize = new Vector2(480, 0) };
        column.AddChild(_needs);
        _needs.Bind(wellbeing, _text);

        _activities = new ActivityBar { CurrentDate = GameBootstrap.Instance.Time.CurrentDate };
        column.AddChild(_activities);
        _activities.Bind(wellbeing, _text, GameBootstrap.Instance.CurrentLocation);
        _activities.ActivityPerformed += OnActivityPerformed;

        // Which activities exist depends on where the human is standing, so the bar re-filters on
        // every move rather than being rebuilt by whoever triggered the travel.
        GameBootstrap.Instance.LocationChanged += OnLocationChanged;
        // A career-role switch replaces the service; re-bind rather than holding the orphaned one.
        GameBootstrap.Instance.WellbeingReplaced += OnWellbeingReplaced;

        _log = new Label
        {
            ThemeTypeVariation = UiTheme.VariationMuted,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_log);

        GD.Print($"[LifeSimScene] Isometric life-sim loaded for a {wellbeing.Role} career.");
    }

    public override void _ExitTree()
    {
        if (GameBootstrap.Instance is null)
            return;

        GameBootstrap.Instance.LocationChanged -= OnLocationChanged;
        GameBootstrap.Instance.WellbeingReplaced -= OnWellbeingReplaced;
    }

    private void OnWellbeingReplaced(IWellbeingService wellbeing)
    {
        _needs.Bind(wellbeing, _text);
        _activities.Bind(wellbeing, _text, GameBootstrap.Instance.CurrentLocation);
    }

    public override void _Process(double delta) =>
        _activities.CurrentDate = GameBootstrap.Instance.Time.CurrentDate;

    private void OnLocationChanged(WorldLocation location) => _activities.SetLocation(location);

    /// <summary>
    /// Report what the activity actually cost and gave. The applied deltas are post-clamp, so a
    /// full night's sleep on an already-rested character honestly reports the smaller gain.
    /// </summary>
    private void OnActivityPerformed(ActivityOutcome outcome)
    {
        IEnumerable<string> parts = outcome.AppliedDeltas.Select(delta =>
            $"{(delta.Delta >= 0 ? "+" : "")}{delta.Delta:0} {_text.Get(LocKeys.NeedShort(delta.Need))}");

        string name = _text.Get(LocKeys.ActivityName(outcome.ActivityKey));
        string summary = string.Join("  ", parts);
        _log.Text = summary.Length == 0
            ? $"{name}: {_text.Get(LocKeys.PanelNoChange)}"
            : $"{name}: {summary}";
    }
}
