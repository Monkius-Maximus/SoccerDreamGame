using System.Globalization;

namespace SoccerSim.Core.World.Fields;

/// <summary>
/// Parses the text a client sends into the type a field holds, and formats it back. Every patch
/// goes through here, which is what makes "the client is never trusted" true rather than
/// aspirational: an enum arrives as a string and either matches a member of the closed set or is
/// refused by name.
/// </summary>
internal static class FieldValue
{
    public static string RequireText(string path, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new FieldPatchException(path, "must not be empty")
            : value.Trim();

    public static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static int Int(string path, string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw new FieldPatchException(path, $"'{value}' is not a whole number");
        return parsed;
    }

    public static int IntInRange(string path, string? value, int min, int max)
    {
        int parsed = Int(path, value);
        if (parsed < min || parsed > max)
            throw new FieldPatchException(path, $"{parsed} is outside {min}–{max}");
        return parsed;
    }

    /// <summary>Accepts both "0.62" and "0,62": the tool's users write decimals with a comma, and
    /// refusing their own notation would be a bug wearing a validation costume.</summary>
    public static double Float(string path, string? value)
    {
        string normalized = (value ?? string.Empty).Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            throw new FieldPatchException(path, $"'{value}' is not a number");
        return parsed;
    }

    public static double? OptionalFloat(string path, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Float(path, value);

    /// <summary>A colour is exactly "#RRGGBB" — the schema's CHECK says so, and every colour
    /// calculation in the tool assumes it.</summary>
    public static string Color(string path, string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;

        if (text.Length != 7 || !text[1..].All(Uri.IsHexDigit))
            throw new FieldPatchException(path, $"'{value}' is not a colour in #RRGGBB form");

        return text.ToUpperInvariant();
    }

    public static TEnum Enum<TEnum>(string path, string? value)
        where TEnum : struct, Enum
    {
        if (System.Enum.TryParse(value, ignoreCase: false, out TEnum parsed) && System.Enum.IsDefined(parsed))
            return parsed;

        throw new FieldPatchException(path,
            $"'{value}' is not a valid {typeof(TEnum).Name} (allowed: {string.Join(", ", System.Enum.GetNames<TEnum>())})");
    }

    /// <summary>The source data encodes booleans as 0/1 and so does the schema; the UI shows them
    /// that way too, so the same spelling is accepted here.</summary>
    public static bool Flag(string path, string? value) => IntInRange(path, value, 0, 1) == 1;

    public static DateOnly Date(string path, string? value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
            throw new FieldPatchException(path, $"'{value}' is not a date in yyyy-MM-dd form");
        return parsed;
    }

    public static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(bool value) => value ? "1" : "0";
}
