using System.Text;

namespace SoccerSim.Content;

/// <summary>
/// Helpers for the stable, human-readable keys every authored entity carries.
///
/// Keys are what the JSON bundle uses for cross-references (a team names its league by key,
/// never by number), so they must survive editing sessions, branches and merges untouched.
/// The numeric ids the game runs on are carried alongside — see <see cref="IContentEntity.Id"/>.
/// </summary>
public static class ContentKey
{
    public const int MaxLength = 96;

    /// <summary>
    /// True if <paramref name="key"/> is well-formed: lowercase a–z, digits, and the
    /// separators <c>-</c>, <c>_</c>, <c>.</c>; must start with a letter and must not end
    /// with a separator. Restrictive on purpose — keys end up in filenames, URLs and diffs.
    /// </summary>
    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxLength)
            return false;
        if (key[0] is < 'a' or > 'z')
            return false;
        if (IsSeparator(key[^1]))
            return false;

        foreach (char c in key)
        {
            bool allowed = c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || IsSeparator(c);
            if (!allowed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Best-effort slug of a display name, for the authoring tool to propose a key from
    /// something the user already typed. Never silently trusted — the result still goes
    /// through <see cref="IsValid"/>, and the user can overwrite it.
    /// </summary>
    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (char raw in text.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(raw)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;   // strip the accent, keep the base letter ("José" -> "jose")
            }

            char c = char.ToLowerInvariant(raw);
            if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9')
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        while (builder.Length > 0 && IsSeparator(builder[^1]))
            builder.Length--;

        // A key must start with a letter; a name that slugs to "1899" gets a prefix instead
        // of being rejected outright.
        if (builder.Length > 0 && builder[0] is < 'a' or > 'z')
            builder.Insert(0, 'x');

        if (builder.Length > MaxLength)
            builder.Length = MaxLength;
        while (builder.Length > 0 && IsSeparator(builder[^1]))
            builder.Length--;

        return builder.ToString();
    }

    private static bool IsSeparator(char c) => c is '-' or '_' or '.';
}
