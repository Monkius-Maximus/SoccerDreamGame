using System.Text;

namespace SoccerSim.Core.World.Serialization;

/// <summary>
/// The CSV dialect the workbook speaks (ALGORITHMS.md §8): semicolon-separated, because that is
/// what Excel pt-BR opens without an import wizard; arrays joined with <c>|</c>; and any field
/// containing a quote, a semicolon or a newline wrapped in double quotes with <c>""</c> escaping.
///
/// <para>Hand-rolled rather than taken from a package because <c>SoccerSim.Core</c> takes no
/// NuGet dependency, and because the dialect is four rules — a parser for it is smaller than the
/// configuration a general-purpose library would need.</para>
/// </summary>
public static class Csv
{
    public const char Separator = ';';

    /// <summary>How a multi-valued cell joins its parts (<c>WG|ST</c>).</summary>
    public const char ArraySeparator = '|';

    /// <summary>
    /// Excel on Windows reads a UTF-8 file as the system code page unless it starts with a byte
    /// order mark, which turns every accented club name into mojibake. The tool's whole reason for
    /// writing CSV is that the file opens in Excel, so it writes the mark.
    /// </summary>
    public const string ByteOrderMark = "﻿";

    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        WriteRow(builder, header);

        foreach (IReadOnlyList<string?> row in rows)
            WriteRow(builder, row);

        return builder.ToString();
    }

    private static void WriteRow(StringBuilder builder, IReadOnlyList<string?> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (i > 0)
                builder.Append(Separator);
            builder.Append(Escape(row[i]));
        }

        // \r\n, not \n: the file is written for Excel first and read by this tool second, and the
        // reader accepts either.
        builder.Append("\r\n");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.IndexOf('"') < 0 && value.IndexOf(Separator) < 0
            && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    /// <summary>
    /// Splits a document into rows of cells. Quotes are honoured, so a field may contain
    /// separators and newlines; a byte order mark and either line ending are accepted. Blank
    /// trailing lines are dropped, because every editor adds one.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(string document)
    {
        if (document.StartsWith(ByteOrderMark, StringComparison.Ordinal))
            document = document[ByteOrderMark.Length..];

        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();

        bool quoted = false;
        bool any = false;      // whether the current row has seen any character at all

        for (int i = 0; i < document.Length; i++)
        {
            char c = document[i];

            if (quoted)
            {
                if (c != '"')
                {
                    cell.Append(c);
                    continue;
                }

                // A doubled quote inside a quoted field is a literal quote; a single one ends it.
                if (i + 1 < document.Length && document[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                    continue;
                }

                quoted = false;
                continue;
            }

            switch (c)
            {
                case '"' when cell.Length == 0:
                    quoted = true;
                    any = true;
                    break;

                case Separator:
                    row.Add(cell.ToString());
                    cell.Clear();
                    any = true;
                    break;

                case '\r':
                    break;      // swallowed; the \n that follows ends the row

                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = [];
                    any = false;
                    break;

                default:
                    cell.Append(c);
                    any = true;
                    break;
            }
        }

        // A last line with no trailing newline is still a row.
        if (any || cell.Length > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Splits a multi-valued cell. An empty cell is no values, not one empty value.</summary>
    public static IReadOnlyList<string> SplitArray(string? cell) =>
        string.IsNullOrWhiteSpace(cell)
            ? []
            : cell.Split(ArraySeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static string JoinArray(IEnumerable<string> values) => string.Join(ArraySeparator, values);
}
