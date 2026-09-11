using System.Globalization;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// How a single value is spelled in the tool's CSV. One dialect, stated once: the separator is
/// already <c>;</c> because Excel pt-BR expects it, and the same choice makes <c>,</c> the decimal
/// mark. Writing <c>0.86</c> instead would make Excel show the number as text, which defeats the
/// only reason the tool writes CSV at all.
///
/// <para>The reader is strict about this — a cell spelled <c>0.86</c> is rejected by name and
/// line rather than quietly parsed. Two accepted spellings of one number is how a column silently
/// changes meaning halfway down a file.</para>
/// </summary>
public static class CsvValue
{
    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");

    public const string DateFormat = "yyyy-MM-dd";

    public static string Text(string? value) => value ?? string.Empty;

    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(double value) => value.ToString("0.############", Ptbr);

    public static string Number(double? value) => value is null ? string.Empty : Number(value.Value);

    /// <summary>0/1, the way the workbook and the JSON document both spell a flag.</summary>
    public static string Flag(bool value) => value ? "1" : "0";

    public static string Date(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string Enum<TEnum>(TEnum value) where TEnum : struct, System.Enum => value.ToString()!;
}
