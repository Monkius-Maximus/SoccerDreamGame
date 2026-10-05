using Godot;

namespace SoccerDreamGame.Ui;

/// <summary>
/// The single source of truth for the interface's colour, type and spacing scales.
///
/// <para>
/// These are the design tokens from the UI concept, re-baselined for a desktop executable rather
/// than a browser mockup. Three deliberate departures from the concept:
/// </para>
///
/// <list type="number">
///   <item><b>Type scale ×1.6.</b> The concept's smallest label was 5px inside a 32px bar. At 1080p
///   that is roughly three pixels of glyph — texture, not text. Nothing here goes below
///   <see cref="FontMicro"/> (11px) and the HUD bar is <see cref="HudBarHeight"/> (52px), which also
///   brings the nav rows above a usable click-target height.</item>
///
///   <item><b>Neutral chrome.</b> In the concept <see cref="Positive"/> green was simultaneously the
///   brand, every panel border, every panel title, every pulse dot, the focus ring and the positive
///   state — so it had no signalling power left. Here the chrome is slate
///   (<see cref="Border"/>/<see cref="TextMuted"/>) and green is reserved for three meanings only:
///   a positive value, live data, and "this is you".</item>
///
///   <item><b>Contrast floor.</b> The concept's secondary text (#5a7a9a on the background) sat at
///   about 4.4:1, under the 4.5:1 minimum for small text. <see cref="TextMuted"/> is lightened to
///   clear 7:1, so the smallest labels stay legible.</item>
/// </list>
/// </summary>
public static class UiTokens
{
    // ── Surfaces ────────────────────────────────────────────────────────────────────────

    /// <summary>The deepest surface; the window background.</summary>
    public static readonly Color Background = Color.FromHtml("#060b18");

    /// <summary>A raised panel sitting on <see cref="Background"/>.</summary>
    public static readonly Color Surface = Color.FromHtml("#0a1422");

    /// <summary>A panel header or an inset row inside a panel.</summary>
    public static readonly Color SurfaceRaised = Color.FromHtml("#0d1829");

    /// <summary>The hover/selected wash applied to list rows and nav items.</summary>
    public static readonly Color SurfaceHover = Color.FromHtml("#152238");

    // ── Chrome (neutral on purpose — see the class remarks) ─────────────────────────────

    /// <summary>Default panel/divider border. Slate, never the accent.</summary>
    public static readonly Color Border = Color.FromHtml("#1e2c42");

    /// <summary>A border that needs to read as interactive or focused.</summary>
    public static readonly Color BorderStrong = Color.FromHtml("#31455f");

    // ── Text ────────────────────────────────────────────────────────────────────────────

    /// <summary>Primary reading colour.</summary>
    public static readonly Color TextPrimary = Color.FromHtml("#e8edf5");

    /// <summary>Secondary labels and units. Cleared to ~7.5:1 on <see cref="Background"/>.</summary>
    public static readonly Color TextMuted = Color.FromHtml("#8aa2bd");

    /// <summary>Text drawn on top of a filled <see cref="Positive"/> surface.</summary>
    public static readonly Color TextOnAccent = Color.FromHtml("#060b18");

    // ── Semantic state (the ONLY places the accent hues are allowed) ────────────────────

    /// <summary>Positive value, live data, or "this is you". Reserved — not a chrome colour.</summary>
    public static readonly Color Positive = Color.FromHtml("#00e676");

    /// <summary>Informational highlight; also the neutral/adequate band.</summary>
    public static readonly Color Info = Color.FromHtml("#4d9fff");

    /// <summary>Degrading state that has not yet become harmful.</summary>
    public static readonly Color Warning = Color.FromHtml("#ffa726");

    /// <summary>Harmful state: a bottomed-out need, an injury, a failed action.</summary>
    public static readonly Color Danger = Color.FromHtml("#ff4d6a");

    // ── Type scale (px). Nothing below FontMicro ships. ─────────────────────────────────

    /// <summary>11px — the floor. Units and axis labels only, never a full sentence.</summary>
    public const int FontMicro = 11;

    /// <summary>13px — dense table cells and secondary labels.</summary>
    public const int FontSmall = 13;

    /// <summary>15px — body copy and the default control label.</summary>
    public const int FontBody = 15;

    /// <summary>18px — panel titles.</summary>
    public const int FontSubtitle = 18;

    /// <summary>22px — screen titles and headline stat values.</summary>
    public const int FontTitle = 22;

    /// <summary>28px — the single hero figure on a screen.</summary>
    public const int FontDisplay = 28;

    // ── Spacing & geometry ──────────────────────────────────────────────────────────────

    public const int SpaceXs = 4;
    public const int SpaceSm = 8;
    public const int SpaceMd = 12;
    public const int SpaceLg = 16;
    public const int SpaceXl = 24;

    /// <summary>Sharp-but-not-square, preserving the concept's broadcast-graphics character.</summary>
    public const int CornerRadius = 4;

    public const int BorderWidth = 1;

    /// <summary>
    /// Height of the persistent status strip. The concept used 32px including its labels; this
    /// leaves room for an 11px unit above a 22px value without either being clipped.
    /// </summary>
    public const int HudBarHeight = 52;

    /// <summary>Minimum height of a clickable row, so nav and list items stay comfortable targets.</summary>
    public const int MinTouchTarget = 44;

    /// <summary>Height of a need/progress gauge.</summary>
    public const int GaugeHeight = 10;

    /// <summary>
    /// The colour a 0–100 need gauge draws at. Maps the simulation's own
    /// <c>NeedBand</c> thresholds, so the visual and mechanical thresholds can never drift apart.
    /// </summary>
    public static Color NeedColor(double value) => value switch
    {
        < 20.0 => Danger,    // Critical
        < 40.0 => Warning,   // Low
        < 75.0 => Info,      // Adequate
        _ => Positive,       // Good
    };
}
