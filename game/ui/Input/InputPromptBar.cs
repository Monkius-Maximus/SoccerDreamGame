using Godot;
using SoccerSim.Core.Localization;

namespace SoccerDreamGame.Ui.Input;

/// <summary>One verb a screen offers, and the label to show beside its glyph.</summary>
/// <param name="Verb">The abstract action.</param>
/// <param name="LabelKey">Localisation key for what the verb does <i>on this screen</i>.</param>
public readonly record struct InputPrompt(InputVerb Verb, string LabelKey);

/// <summary>
/// The footer strip of contextual button hints — the piece that makes the game navigable with a
/// controller instead of only a mouse.
///
/// <para>
/// A screen declares what its verbs mean and nothing more:
/// <c>[ (Confirm, "prompt.select"), (Menu, "prompt.formation"), (Back, "prompt.exit") ]</c>. The
/// glyphs come from <see cref="GameInput"/> and swap the instant the human touches a different
/// device, so no screen ever hardcodes "Esc" or "Start".
/// </para>
/// </summary>
public partial class InputPromptBar : PanelContainer
{
    private readonly List<InputPrompt> _prompts = [];

    private HBoxContainer _row = null!;
    private ILocalizer _text = null!;
    private InputDevice _device = InputDevice.Keyboard;

    public override void _Ready()
    {
        Theme = UiTheme.Instance;

        var style = new StyleBoxFlat { BgColor = UiTokens.SurfaceRaised };
        style.BorderColor = UiTokens.Border;
        style.BorderWidthTop = UiTokens.BorderWidth;
        style.SetContentMarginAll(UiTokens.SpaceSm);
        AddThemeStyleboxOverride("panel", style);

        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", UiTokens.SpaceXl);
        _row.Alignment = BoxContainer.AlignmentMode.End;
        AddChild(_row);
    }

    /// <summary>Set the verbs this screen offers. Replaces whatever was shown before.</summary>
    public void Show(ILocalizer text, IEnumerable<InputPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(prompts);

        _text = text;
        _prompts.Clear();
        _prompts.AddRange(prompts);
        Rebuild();
    }

    /// <summary>Redraw for a new device. Called when the human switches between keyboard and pad.</summary>
    public void SetDevice(InputDevice device)
    {
        if (_device == device)
            return;

        _device = device;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_row is null || !IsInstanceValid(_row))
            return;

        foreach (Node child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }

        foreach (InputPrompt prompt in _prompts)
            _row.AddChild(BuildPrompt(prompt));
    }

    private Control BuildPrompt(InputPrompt prompt)
    {
        var group = new HBoxContainer();
        group.AddThemeConstantOverride("separation", UiTokens.SpaceSm);

        // The glyph sits in a bordered chip so it reads as a key cap rather than as more prose.
        var chip = new PanelContainer();
        var chipStyle = new StyleBoxFlat { BgColor = UiTokens.Surface };
        chipStyle.BorderColor = UiTokens.BorderStrong;
        chipStyle.SetBorderWidthAll(UiTokens.BorderWidth);
        chipStyle.SetCornerRadiusAll(UiTokens.CornerRadius);
        chipStyle.SetContentMarginAll(UiTokens.SpaceXs);
        chip.AddThemeStyleboxOverride("panel", chipStyle);

        var glyph = new Label
        {
            Text = GameInput.Glyph(prompt.Verb, _device),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        glyph.AddThemeColorOverride("font_color", GameInput.GlyphColor(prompt.Verb, _device));
        glyph.AddThemeFontSizeOverride("font_size", UiTokens.FontSmall);
        chip.AddChild(glyph);
        group.AddChild(chip);

        group.AddChild(new Label
        {
            Text = _text.Get(prompt.LabelKey),
            ThemeTypeVariation = UiTheme.VariationMuted,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return group;
    }
}
