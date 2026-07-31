using Godot;

namespace SoccerDreamGame.Ui;

/// <summary>
/// Builds the game's Godot <see cref="Theme"/> from <see cref="UiTokens"/> in code.
///
/// <para>
/// Built programmatically rather than authored as a <c>.tres</c> so the design system is
/// reviewable in a diff and can never drift from the tokens: change a token and every control
/// follows. The concept prototype expressed this as CSS custom properties and Tailwind classes,
/// none of which survive the move to a compiled executable — this is that same system in the form
/// the engine actually consumes.
/// </para>
///
/// <para>
/// Type variations are the equivalent of the prototype's utility classes: assign
/// <c>ThemeTypeVariation</c> on a <see cref="Label"/> to get the panel-title or muted-caption
/// treatment without restating colours at the call site.
/// </para>
/// </summary>
public static class UiTheme
{
    /// <summary>A panel title: uppercase-styled heading inside a panel header.</summary>
    public const string VariationPanelTitle = "PanelTitle";

    /// <summary>A headline number in the HUD or a stat tile.</summary>
    public const string VariationStatValue = "StatValue";

    /// <summary>The small unit caption under a stat value. The only place the 11px floor is used.</summary>
    public const string VariationStatUnit = "StatUnit";

    /// <summary>De-emphasised secondary copy.</summary>
    public const string VariationMuted = "Muted";

    /// <summary>Text carrying a harmful state (injury, bottomed-out need).</summary>
    public const string VariationDanger = "Danger";

    /// <summary>Text carrying a positive state, or marking the human's own row.</summary>
    public const string VariationPositive = "Positive";

    private static Theme? _instance;

    /// <summary>The shared theme instance, built once per run.</summary>
    public static Theme Instance => _instance ??= Build();

    /// <summary>
    /// Assemble the theme. Every colour and size comes from <see cref="UiTokens"/>; nothing is
    /// hard-coded here.
    /// </summary>
    public static Theme Build()
    {
        var theme = new Theme
        {
            DefaultFontSize = UiTokens.FontBody,
        };

        // ── Label ───────────────────────────────────────────────────────────────────────
        theme.SetColor("font_color", "Label", UiTokens.TextPrimary);
        theme.SetFontSize("font_size", "Label", UiTokens.FontBody);

        theme.SetTypeVariation(VariationPanelTitle, "Label");
        theme.SetColor("font_color", VariationPanelTitle, UiTokens.TextPrimary);
        theme.SetFontSize("font_size", VariationPanelTitle, UiTokens.FontSubtitle);

        theme.SetTypeVariation(VariationStatValue, "Label");
        theme.SetColor("font_color", VariationStatValue, UiTokens.TextPrimary);
        theme.SetFontSize("font_size", VariationStatValue, UiTokens.FontTitle);

        theme.SetTypeVariation(VariationStatUnit, "Label");
        theme.SetColor("font_color", VariationStatUnit, UiTokens.TextMuted);
        theme.SetFontSize("font_size", VariationStatUnit, UiTokens.FontMicro);

        theme.SetTypeVariation(VariationMuted, "Label");
        theme.SetColor("font_color", VariationMuted, UiTokens.TextMuted);
        theme.SetFontSize("font_size", VariationMuted, UiTokens.FontSmall);

        theme.SetTypeVariation(VariationDanger, "Label");
        theme.SetColor("font_color", VariationDanger, UiTokens.Danger);
        theme.SetFontSize("font_size", VariationDanger, UiTokens.FontSmall);

        theme.SetTypeVariation(VariationPositive, "Label");
        theme.SetColor("font_color", VariationPositive, UiTokens.Positive);
        theme.SetFontSize("font_size", VariationPositive, UiTokens.FontSmall);

        // ── Panels ──────────────────────────────────────────────────────────────────────
        theme.SetStylebox("panel", "PanelContainer", Panel(UiTokens.Surface));
        theme.SetStylebox("panel", "Panel", Panel(UiTokens.Surface));

        // ── Buttons ─────────────────────────────────────────────────────────────────────
        theme.SetStylebox("normal", "Button", Panel(UiTokens.SurfaceRaised));
        theme.SetStylebox("hover", "Button", Panel(UiTokens.SurfaceHover, UiTokens.BorderStrong));
        theme.SetStylebox("pressed", "Button", Panel(UiTokens.SurfaceHover, UiTokens.Positive));
        theme.SetStylebox("focus", "Button", Panel(new Color(0, 0, 0, 0), UiTokens.Positive));
        theme.SetStylebox("disabled", "Button", Panel(UiTokens.Surface));
        theme.SetColor("font_color", "Button", UiTokens.TextPrimary);
        theme.SetColor("font_hover_color", "Button", UiTokens.TextPrimary);
        theme.SetColor("font_pressed_color", "Button", UiTokens.Positive);
        theme.SetColor("font_disabled_color", "Button", UiTokens.TextMuted);
        theme.SetFontSize("font_size", "Button", UiTokens.FontBody);

        // ── Gauges ──────────────────────────────────────────────────────────────────────
        // The fill colour is set per-instance from the need's own band, so only the trough is
        // themed here (see NeedsPanel).
        theme.SetStylebox("background", "ProgressBar", Flat(UiTokens.SurfaceRaised));
        theme.SetStylebox("fill", "ProgressBar", Flat(UiTokens.Info));
        theme.SetFontSize("font_size", "ProgressBar", UiTokens.FontMicro);

        // ── Dialogs ─────────────────────────────────────────────────────────────────────
        theme.SetStylebox("panel", "AcceptDialog", Panel(UiTokens.Surface, UiTokens.BorderStrong));
        theme.SetStylebox("embedded_border", "Window", Panel(UiTokens.Surface, UiTokens.BorderStrong));
        theme.SetColor("title_color", "Window", UiTokens.TextPrimary);

        return theme;
    }

    /// <summary>A bordered, padded surface — the standard panel treatment.</summary>
    private static StyleBoxFlat Panel(Color background, Color? border = null)
    {
        var style = Flat(background);
        style.BorderColor = border ?? UiTokens.Border;
        style.SetBorderWidthAll(UiTokens.BorderWidth);
        style.SetContentMarginAll(UiTokens.SpaceMd);
        return style;
    }

    /// <summary>A borderless fill with the shared corner radius.</summary>
    private static StyleBoxFlat Flat(Color background)
    {
        var style = new StyleBoxFlat { BgColor = background };
        style.SetCornerRadiusAll(UiTokens.CornerRadius);
        return style;
    }
}
