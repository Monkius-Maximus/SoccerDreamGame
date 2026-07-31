using Godot;
using SoccerSim.Core.LifeSim;

namespace SoccerDreamGame.Ui;

/// <summary>
/// The six off-pitch need gauges, bound to the live <see cref="IWellbeingService"/>.
///
/// <para>
/// This is the panel the UI concept drew but never wired: there, the bars were local component
/// state and only one of the six was connected to anything, so five of them moved without meaning.
/// Here every gauge reads <see cref="WellbeingState"/> directly and the panel repaints from the
/// service's <see cref="IWellbeingService.Changed"/> event, so a bar can only move because the
/// simulation moved it.
/// </para>
///
/// <para>
/// The same panel serves both career roles. It labels itself from
/// <see cref="IWellbeingService.Role"/> and the gauges are identical, because the needs are
/// identical — what differs is how fast they drain, which lives in the core's need profile.
/// </para>
/// </summary>
public partial class NeedsPanel : PanelContainer
{
    private readonly Dictionary<NeedKind, ProgressBar> _bars = new();
    private readonly Dictionary<NeedKind, Label> _values = new();

    private IWellbeingService? _service;
    private Label _indexValue = null!;
    private Label _roleLabel = null!;
    private Label _alertLabel = null!;

    /// <summary>
    /// Attach to a career's wellbeing. Safe to call before or after the node enters the tree; the
    /// controls are built on the first bind.
    /// </summary>
    public void Bind(IWellbeingService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (_service is not null)
            _service.Changed -= OnWellbeingChanged;

        _service = service;
        _service.Changed += OnWellbeingChanged;

        if (_bars.Count == 0)
            BuildControls();

        Refresh(_service.Snapshot);
    }

    public override void _ExitTree()
    {
        if (_service is not null)
            _service.Changed -= OnWellbeingChanged;
    }

    private void BuildControls()
    {
        Theme = UiTheme.Instance;

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        AddChild(root);

        // ── Header: the one headline number, plus which career it describes ──────────────
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        root.AddChild(header);

        var title = new Label { Text = "WELLBEING", ThemeTypeVariation = UiTheme.VariationPanelTitle };
        header.AddChild(title);

        _roleLabel = new Label
        {
            ThemeTypeVariation = UiTheme.VariationMuted,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(_roleLabel);

        _indexValue = new Label
        {
            ThemeTypeVariation = UiTheme.VariationStatValue,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        header.AddChild(_indexValue);

        root.AddChild(new HSeparator());

        // ── One row per need ────────────────────────────────────────────────────────────
        foreach (NeedKind need in Needs.All)
            root.AddChild(BuildNeedRow(need));

        // ── Alerts: only rendered when the simulation actually reports one ───────────────
        _alertLabel = new Label
        {
            ThemeTypeVariation = UiTheme.VariationDanger,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        };
        root.AddChild(_alertLabel);
    }

    private Control BuildNeedRow(NeedKind need)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTokens.SpaceSm);
        row.CustomMinimumSize = new Vector2(0, UiTokens.MinTouchTarget * 0.6f);

        var name = new Label
        {
            Text = need.ToString().ToUpperInvariant(),
            ThemeTypeVariation = UiTheme.VariationMuted,
            CustomMinimumSize = new Vector2(96, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(name);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            ShowPercentage = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, UiTokens.GaugeHeight),
        };
        row.AddChild(bar);
        _bars[need] = bar;

        // The numeric value is not decoration: colour alone must never be the only carrier of a
        // need's state, so the number stays readable for colour-blind players.
        var value = new Label
        {
            ThemeTypeVariation = UiTheme.VariationMuted,
            CustomMinimumSize = new Vector2(40, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(value);
        _values[need] = value;

        return row;
    }

    private void OnWellbeingChanged(WellbeingSnapshot snapshot) => Refresh(snapshot);

    private void Refresh(WellbeingSnapshot snapshot)
    {
        if (_service is null)
            return;

        _roleLabel.Text = snapshot.Role == CareerRole.Player ? "Player career" : "Manager career";
        _indexValue.Text = $"{snapshot.Index:0}";
        _indexValue.AddThemeColorOverride("font_color", UiTokens.NeedColor(snapshot.Index));

        foreach (NeedKind need in Needs.All)
        {
            double value = _service.State[need];
            Color colour = UiTokens.NeedColor(value);

            ProgressBar bar = _bars[need];
            bar.Value = value;
            var fill = new StyleBoxFlat { BgColor = colour };
            fill.SetCornerRadiusAll(UiTokens.CornerRadius);
            bar.AddThemeStyleboxOverride("fill", fill);

            Label label = _values[need];
            label.Text = $"{value:0}";
            label.AddThemeColorOverride("font_color", colour);
        }

        if (snapshot.HasCriticalNeed)
        {
            _alertLabel.Visible = true;
            _alertLabel.Text = $"Critical: {string.Join(", ", snapshot.CriticalNeeds)}";
        }
        else
        {
            _alertLabel.Visible = false;
        }
    }
}
