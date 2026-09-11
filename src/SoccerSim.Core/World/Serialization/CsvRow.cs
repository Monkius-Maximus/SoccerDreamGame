using System.Globalization;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// One row of an imported tab, which knows where it came from. Every failure reports
/// <c>Clubes.csv:14 · stadiumCapacity</c> rather than "input string was not in a correct format",
/// because a spreadsheet with 688 rows in it is unfixable without that.
///
/// <para>Strict in the same way <see cref="JsonCursor"/> is: a missing or wrongly-typed cell
/// throws rather than defaulting. A silently defaulted club is worse than a rejected one.</para>
/// </summary>
public sealed class CsvRow
{
    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IReadOnlyDictionary<string, int> _columns;
    private readonly IReadOnlyList<string> _cells;
    private readonly string _tab;

    public CsvRow(string tab, int line, IReadOnlyDictionary<string, int> columns, IReadOnlyList<string> cells)
    {
        _tab = tab;
        _columns = columns;
        _cells = cells;
        Line = line;
    }

    /// <summary>The line in the file, 1-based and counting the header — what the editor shows.</summary>
    public int Line { get; }

    public string Where(string column) => $"{_tab}.csv:{Line} · {column}";

    public bool Has(string column) => _columns.ContainsKey(column);

    /// <summary>The raw cell, or null when the column is absent or blank.</summary>
    public string? Raw(string column)
    {
        if (!_columns.TryGetValue(column, out int index) || index >= _cells.Count)
            return null;

        string value = _cells[index];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string String(string column) =>
        Raw(column) ?? throw new WorldFieldException(Where(column), "must not be empty");

    public string? OptionalString(string column) => Raw(column);

    public int Int(string column)
    {
        string raw = String(column);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new WorldFieldException(Where(column), $"'{raw}' is not a whole number");
    }

    /// <summary>
    /// A decimal in the tool's dialect: comma-separated, because the file is written for Excel
    /// pt-BR. A cell spelled <c>0.86</c> is rejected by name rather than parsed as 86.
    /// </summary>
    public double Double(string column)
    {
        string raw = String(column);
        if (double.TryParse(raw, NumberStyles.Float, Ptbr, out double value))
            return value;

        // NumberStyles.Float excludes thousands separators, so "0.86" fails to parse here rather
        // than becoming 86 — and the message says what to write instead.
        throw new WorldFieldException(Where(column),
            raw.Contains('.')
                ? $"'{raw}' uses a dot as the decimal mark; this file uses a comma ({raw.Replace('.', ',')})"
                : $"'{raw}' is not a number");
    }

    public double? OptionalDouble(string column) => Raw(column) is null ? null : Double(column);

    public bool Flag(string column)
    {
        string raw = String(column);
        return raw switch
        {
            "1" => true,
            "0" => false,
            _ => throw new WorldFieldException(Where(column), $"expected 0 or 1, found '{raw}'"),
        };
    }

    public DateOnly Date(string column)
    {
        string raw = String(column);
        return DateOnly.TryParseExact(raw, CsvValue.DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateOnly value)
            ? value
            : throw new WorldFieldException(Where(column), $"'{raw}' is not a date (expected {CsvValue.DateFormat})");
    }

    public TEnum Enum<TEnum>(string column) where TEnum : struct, System.Enum
    {
        string raw = String(column);
        if (System.Enum.TryParse(raw, ignoreCase: false, out TEnum parsed) && System.Enum.IsDefined(parsed))
            return parsed;

        throw new WorldFieldException(Where(column),
            $"'{raw}' is not a valid {typeof(TEnum).Name} (allowed: {string.Join(", ", System.Enum.GetNames<TEnum>())})");
    }

    public IReadOnlyList<TEnum> EnumList<TEnum>(string column) where TEnum : struct, System.Enum
    {
        var values = new List<TEnum>();
        foreach (string part in Csv.SplitArray(Raw(column)))
        {
            if (!System.Enum.TryParse(part, ignoreCase: false, out TEnum parsed) || !System.Enum.IsDefined(parsed))
            {
                throw new WorldFieldException(Where(column),
                    $"'{part}' is not a valid {typeof(TEnum).Name} (allowed: {string.Join(", ", System.Enum.GetNames<TEnum>())})");
            }

            values.Add(parsed);
        }

        return values;
    }

    public IReadOnlyList<string> StringList(string column) => Csv.SplitArray(Raw(column));
}
