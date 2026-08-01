using Godot;

namespace SoccerDreamGame.Ui.Input;

/// <summary>The abstract verbs the interface exposes, independent of any physical button.</summary>
public enum InputVerb
{
    /// <summary>Select / activate. Enter · A · ✕</summary>
    Confirm,

    /// <summary>Back / cancel. Esc · B · ○</summary>
    Back,

    /// <summary>Secondary action, screen-specific. F · X · □</summary>
    Alt,

    /// <summary>Open the quick menu. Esc · Start</summary>
    Menu,

    /// <summary>Open the in-game phone. P · Y · △</summary>
    Phone,
}

/// <summary>Which family of glyphs to draw. Detected from the last input actually received.</summary>
public enum InputDevice
{
    Keyboard,
    Xbox,
    PlayStation,
}

/// <summary>
/// The input contract: named verbs, the Godot actions behind them, and the glyph each verb shows on
/// the device currently in use.
///
/// <para>
/// Adapted from the reimagined prototype's platform map, which had the right idea — a screen
/// declares <i>verbs</i> ("confirm: Swap", "menu: Formation") and the presentation layer decides
/// what glyph that is. A screen never mentions Esc or Start, so adding a controller is a change
/// here and nowhere else.
/// </para>
///
/// <para>
/// Actions are registered into <see cref="InputMap"/> in code rather than authored in
/// <c>project.godot</c>. The editor's serialised <c>InputEvent</c> blobs are unreviewable in a diff
/// and easy to corrupt by hand; this is explicit, greppable, and does not need the editor open.
/// </para>
/// </summary>
public static class GameInput
{
    public const string ActionConfirm = "game_confirm";
    public const string ActionBack = "game_back";
    public const string ActionAlt = "game_alt";
    public const string ActionMenu = "game_menu";
    public const string ActionPhone = "game_phone";

    /// <summary>The Godot action name backing a verb.</summary>
    public static string ActionOf(InputVerb verb) => verb switch
    {
        InputVerb.Confirm => ActionConfirm,
        InputVerb.Back => ActionBack,
        InputVerb.Alt => ActionAlt,
        InputVerb.Menu => ActionMenu,
        InputVerb.Phone => ActionPhone,
        _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, "Unknown input verb."),
    };

    /// <summary>
    /// Install every action. Idempotent, so a reload cannot end up with duplicate bindings.
    ///
    /// <para>
    /// Esc and Start both map to <see cref="InputVerb.Menu"/>, which is the traditional pairing and
    /// what makes the quick menu reachable the same way on a keyboard and on a pad.
    /// </para>
    /// </summary>
    public static void RegisterActions()
    {
        Bind(ActionConfirm, [Key.Enter, Key.Space], JoyButton.A);
        Bind(ActionBack, [Key.Backspace], JoyButton.B);
        Bind(ActionAlt, [Key.F], JoyButton.X);
        // Esc doubles as "close the thing that is open": an overlay treats Menu as dismiss, so one
        // key opens the quick menu from the world and closes it from inside.
        Bind(ActionMenu, [Key.Escape], JoyButton.Start);
        Bind(ActionPhone, [Key.P], JoyButton.Y);
    }

    /// <summary>The glyph to draw for a verb on a given device.</summary>
    public static string Glyph(InputVerb verb, InputDevice device) => device switch
    {
        InputDevice.Keyboard => verb switch
        {
            InputVerb.Confirm => "Enter",
            InputVerb.Back => "Backspace",
            InputVerb.Alt => "F",
            InputVerb.Menu => "Esc",
            InputVerb.Phone => "P",
            _ => "?",
        },
        InputDevice.Xbox => verb switch
        {
            InputVerb.Confirm => "A",
            InputVerb.Back => "B",
            InputVerb.Alt => "X",
            InputVerb.Menu => "Start",
            InputVerb.Phone => "Y",
            _ => "?",
        },
        InputDevice.PlayStation => verb switch
        {
            InputVerb.Confirm => "✕",
            InputVerb.Back => "○",
            InputVerb.Alt => "□",
            InputVerb.Menu => "Options",
            InputVerb.Phone => "△",
            _ => "?",
        },
        _ => "?",
    };

    /// <summary>Accent colour for a glyph. PlayStation shapes are colour-coded; the rest are neutral.</summary>
    public static Color GlyphColor(InputVerb verb, InputDevice device)
    {
        if (device != InputDevice.PlayStation)
            return UiTokens.TextPrimary;

        return verb switch
        {
            InputVerb.Confirm => UiTokens.Info,
            InputVerb.Back => UiTokens.Danger,
            InputVerb.Phone => UiTokens.Positive,
            _ => UiTokens.TextPrimary,
        };
    }

    private static void Bind(string action, Key[] keys, JoyButton button)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);

        // Clear first: without this a second RegisterActions() (a reload, a test harness) would
        // stack duplicate events onto the same action.
        InputMap.ActionEraseEvents(action);

        foreach (Key key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
    }
}
