namespace SoccerSim.Core.Localization;

/// <summary>
/// The default <see cref="ILocalizer"/>, resolving against <see cref="StringCatalogue"/>.
///
/// <para>
/// Resolution order is active locale → <see cref="StringCatalogue.FallbackLocale"/> → the key
/// itself. Returning the key rather than an empty string is deliberate: a missing translation should
/// be loud in playtest, and <c>activity.shower.name</c> rendered on a button is unmistakable in a
/// way that a blank button is not.
/// </para>
/// </summary>
public sealed class Localizer : ILocalizer
{
    private readonly IReadOnlyDictionary<string, string> _active;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    /// <summary>
    /// Builds a localizer for <paramref name="locale"/>. Throws when the locale is not in the
    /// catalogue — silently falling back to English would ship a Portuguese game in English.
    /// </summary>
    public Localizer(string locale = StringCatalogue.DefaultLocale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);

        if (!StringCatalogue.All.TryGetValue(locale, out IReadOnlyDictionary<string, string>? active))
        {
            throw new ArgumentOutOfRangeException(
                nameof(locale),
                locale,
                $"No catalogue for this locale. Available: {string.Join(", ", StringCatalogue.Locales)}.");
        }

        Locale = locale;
        _active = active;
        _fallback = StringCatalogue.All[StringCatalogue.FallbackLocale];
    }

    public string Locale { get; }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_active.TryGetValue(key, out string? text))
            return text;
        return _fallback.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public bool Has(string key) =>
        !string.IsNullOrWhiteSpace(key) && (_active.ContainsKey(key) || _fallback.ContainsKey(key));
}
