namespace SoccerSim.Core.Localization;

/// <summary>
/// Resolves a stable string key into display text for the active locale.
///
/// <para>
/// The contract that keeps the codebase and the game speaking different languages without either
/// one bending: <b>identifiers stay in English, everything the player reads is a key</b>. The
/// simulation never holds a sentence — <c>LifeActivity</c> carries <c>activity.sleep.name</c>, not
/// "Sleep" and not "Dormir" — so pt-BR is a data change and adding a third locale touches no logic.
/// </para>
///
/// <para>
/// It lives in Core (not the Godot layer) because Core is what owns the keys, which makes their
/// coverage unit-testable without the engine. It has no Godot dependency: a catalogue is a
/// dictionary.
/// </para>
/// </summary>
public interface ILocalizer
{
    /// <summary>The active locale, e.g. <c>pt-BR</c>.</summary>
    string Locale { get; }

    /// <summary>
    /// Display text for <paramref name="key"/>. Falls back to the fallback locale and then to the
    /// key itself — a missing string shows up as <c>activity.sleep.name</c> on screen rather than
    /// as a blank label, so gaps are obvious in playtest instead of invisible.
    /// </summary>
    string Get(string key);

    /// <summary>Shorthand for <see cref="Get"/>.</summary>
    string this[string key] => Get(key);

    /// <summary>True when the active locale (or the fallback) actually defines this key.</summary>
    bool Has(string key);
}
