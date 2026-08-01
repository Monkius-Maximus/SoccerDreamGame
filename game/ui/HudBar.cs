using Godot;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;

namespace SoccerDreamGame.Ui;

/// <summary>
/// The persistent status strip: a row of value/unit tiles that stays on screen across every mode.
///
/// <para>
/// This is the one idea worth lifting wholesale from the UI concept. The game switches between a
/// calendar, a life-sim and a rendered match, and without a fixed strip the human loses track of
/// the simulation state every time the scene changes. Keeping it in an autoload rather than in each
/// scene is what makes it survive <c>ChangeSceneToFile</c>.
/// </para>
///
/// <para>
/// Re-baselined from the concept: tiles are <see cref="UiTokens.FontTitle"/> values over
/// <see cref="UiTokens.FontMicro"/> units inside a <see cref="UiTokens.HudBarHeight"/> bar, where
/// the concept used 10px over 5px inside 32px. Tiles are only added for state that actually exists
/// in the core today — an empty tile is worse than no tile.
/// </para>
/// </summary>
public partial class HudBar : PanelContainer
{
    private readonly Dictionary<string, Label> _tileValues = new();

    private HBoxContainer _tiles = null!;
    private Label _alert = null!;
    private ILocalizer _text = null!;

    /// <summary>Supplies the display text for tile units and the critical-need badge.</summary>
    public void UseLocalizer(ILocalizer text) => _text = text ?? throw new ArgumentNullException(nameof(text));

    public override void _Ready()
    {
        Theme = UiTheme.Instance;
        CustomMinimumSize = new Vector2(0, UiTokens.HudBarHeight);

        var style = new StyleBoxFlat { BgColor = UiTokens.SurfaceRaised };
        style.BorderColor = UiTokens.Border;
        style.BorderWidthBottom = UiTokens.BorderWidth;
        style.SetContentMarginAll(UiTokens.SpaceSm);
        AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTokens.SpaceLg);
        AddChild(row);

        _tiles = new HBoxContainer();
        _tiles.AddThemeConstantOverride("separation", UiTokens.SpaceLg);
        _tiles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_tiles);

        // Right-aligned, and the only element allowed to use Danger — so an injury or a bottomed-out
        // need is the one thing on the strip that can shout.
        _alert = new Label
        {
            ThemeTypeVariation = UiTheme.VariationDanger,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        row.AddChild(_alert);
    }

    /// <summary>Register a tile. <paramref name="key"/> is how <see cref="SetTile"/> addresses it.</summary>
    public void AddTile(string key, string unit)
    {
        var tile = new VBoxContainer();
        tile.AddThemeConstantOverride("separation", 0);

        var value = new Label
        {
            Text = "—",
            ThemeTypeVariation = UiTheme.VariationStatValue,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        tile.AddChild(value);

        tile.AddChild(new Label
        {
            Text = unit.ToUpperInvariant(),
            ThemeTypeVariation = UiTheme.VariationStatUnit,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        _tiles.AddChild(tile);
        _tileValues[key] = value;
    }

    /// <summary>Update a tile's value, optionally recolouring it. Unknown keys are ignored.</summary>
    public void SetTile(string key, string value, Color? colour = null)
    {
        if (!_tileValues.TryGetValue(key, out Label? label))
            return;

        label.Text = value;
        label.AddThemeColorOverride("font_color", colour ?? UiTokens.TextPrimary);
    }

    /// <summary>Show or clear the alert slot. Pass null or empty to hide it.</summary>
    public void SetAlert(string? message)
    {
        _alert.Text = message ?? string.Empty;
        _alert.Visible = !string.IsNullOrEmpty(message);
    }

    /// <summary>
    /// Refresh the wellbeing-derived tiles from a snapshot. The form tile is signed so a negative
    /// modifier reads as the penalty it is.
    /// </summary>
    public void ApplyWellbeing(WellbeingSnapshot snapshot)
    {
        SetTile(TileWellbeing, $"{snapshot.Index:0}", UiTokens.NeedColor(snapshot.Index));
        SetTile(TileForm, $"{snapshot.FormModifier:+0;-0;0}",
            snapshot.FormModifier switch
            {
                > 0 => UiTokens.Positive,
                < 0 => UiTokens.Danger,
                _ => UiTokens.TextMuted,
            });

        if (!snapshot.HasCriticalNeed)
        {
            SetAlert(null);
            return;
        }

        IEnumerable<string> names = snapshot.CriticalNeeds.Select(need => _text.Get(LocKeys.NeedShort(need)));
        SetAlert($"⚕ {_text.Get(LocKeys.HudCritical)} · {string.Join(" · ", names)}");
    }

    /// <summary>Tile key: the current in-game date.</summary>
    public const string TileDate = "date";

    /// <summary>Tile key: the weighted wellbeing index.</summary>
    public const string TileWellbeing = "wellbeing";

    /// <summary>Tile key: the form modifier wellbeing is currently applying.</summary>
    public const string TileForm = "form";

    /// <summary>Tile key: which career is being lived.</summary>
    public const string TileRole = "role";
}
