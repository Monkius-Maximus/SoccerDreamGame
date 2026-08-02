using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoccerSim.Content.Csv;

/// <summary>One row that could not be turned into an entity, addressed by line number.</summary>
public sealed record CsvRowError(int Line, string Message);

public sealed record CsvParseResult(IReadOnlyList<JsonNode> Rows, IReadOnlyList<CsvRowError> Errors);

/// <summary>
/// Converts spreadsheet text into the JSON shape the entity model expects, and back.
///
/// This is the bulk-entry path, and it is what decides whether the tool is usable for a real
/// database: nobody is typing four thousand players into a form. Headers are the JSON field
/// names; a dot addresses a nested object (<c>attributes.pace</c>) and a pipe separates list
/// values (<c>traitKeys</c> = <c>hot_headed|showboat</c>).
///
/// Lives in SoccerSim.Content rather than in the tool so the CLI, the web tool and the tests
/// all share one parser — and so export and import provably round-trip.
/// </summary>
public static class ContentCsv
{
    /// <summary>
    /// Parses delimited text. Accepts both comma and tab so a range pasted straight out of
    /// Excel or Google Sheets works without being saved as a file first.
    /// </summary>
    public static CsvParseResult Parse(string text)
    {
        var rows = new List<JsonNode>();
        var errors = new List<CsvRowError>();

        List<List<string>> lines = Tokenize(text);
        if (lines.Count == 0)
            return new CsvParseResult(rows, errors);

        List<string> headers = lines[0].Select(h => h.Trim()).ToList();
        if (headers.Count == 0 || headers.All(string.IsNullOrEmpty))
        {
            errors.Add(new CsvRowError(1, "The first line must be a header row of field names."));
            return new CsvParseResult(rows, errors);
        }

        for (int i = 1; i < lines.Count; i++)
        {
            List<string> cells = lines[i];
            if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0]))
                continue;   // blank line, not an error

            int lineNumber = i + 1;
            if (cells.Count > headers.Count)
            {
                errors.Add(new CsvRowError(lineNumber,
                    $"{cells.Count} values but only {headers.Count} columns."));
                continue;
            }

            try
            {
                rows.Add(BuildRow(headers, cells));
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                errors.Add(new CsvRowError(lineNumber, ex.Message));
            }
        }

        return new CsvParseResult(rows, errors);
    }

    /// <summary>
    /// Renders entities as CSV with one column per leaf field. Round-trips with
    /// <see cref="Parse"/>, which is what makes "export, edit in a spreadsheet, re-import" safe.
    /// </summary>
    public static string Write<T>(IEnumerable<T> entities, JsonSerializerOptions options)
    {
        // Serialize against the RUNTIME type. With a T of IContentEntity, System.Text.Json would
        // emit only the interface's members and quietly produce a two-column file.
        List<JsonObject> objects = entities
            .Select(e => JsonSerializer.SerializeToNode(e, e?.GetType() ?? typeof(T), options)?.AsObject()
                         ?? throw new InvalidOperationException("Entity serialized to null."))
            .ToList();

        // Union of every leaf path, so a column is emitted even when only some rows populate it.
        var headers = new List<string>();
        foreach (JsonObject item in objects)
            CollectPaths(item, prefix: null, headers);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(Escape)));

        foreach (JsonObject item in objects)
        {
            builder.AppendLine(string.Join(",", headers.Select(header => Escape(ValueAt(item, header)))));
        }

        return builder.ToString();
    }

    private static JsonNode BuildRow(List<string> headers, List<string> cells)
    {
        var row = new JsonObject();

        for (int column = 0; column < headers.Count; column++)
        {
            string header = headers[column];
            if (string.IsNullOrEmpty(header))
                continue;

            string raw = column < cells.Count ? cells[column].Trim() : string.Empty;
            Assign(row, header.Split('.'), raw);
        }

        return row;
    }

    private static void Assign(JsonObject target, string[] path, string raw)
    {
        for (int i = 0; i < path.Length - 1; i++)
        {
            if (target[path[i]] is not JsonObject child)
            {
                child = new JsonObject();
                target[path[i]] = child;
            }

            target = child;
        }

        target[path[^1]] = ToValue(raw);
    }

    /// <summary>
    /// Infers a JSON value from a cell. An empty cell becomes null rather than an empty string,
    /// so a blank optional column means "unset" instead of failing a type conversion later.
    /// </summary>
    private static JsonNode? ToValue(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        if (raw.Contains('|'))
        {
            var array = new JsonArray();
            foreach (string part in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
                array.Add(part.Trim());
            return array;
        }

        if (bool.TryParse(raw, out bool flag))
            return JsonValue.Create(flag);

        if (long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(raw);
    }

    private static void CollectPaths(JsonObject item, string? prefix, List<string> headers)
    {
        foreach ((string name, JsonNode? value) in item)
        {
            string path = prefix is null ? name : prefix + "." + name;
            if (value is JsonObject nested)
                CollectPaths(nested, path, headers);
            else if (!headers.Contains(path))
                headers.Add(path);
        }
    }

    private static string ValueAt(JsonObject item, string path)
    {
        JsonNode? node = item;
        foreach (string segment in path.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(segment, out node))
                return string.Empty;
        }

        return node switch
        {
            null => string.Empty,
            JsonArray array => string.Join("|", array.Select(v => v?.ToString() ?? string.Empty)),
            _ => node.ToString(),
        };
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
            return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    /// <summary>
    /// Splits into rows and cells, honouring RFC 4180 quoting (a quoted field may contain the
    /// delimiter, a newline, or a doubled quote). The delimiter is detected from the header:
    /// a tab wins if present, which is what a paste from a spreadsheet produces.
    /// </summary>
    private static List<List<string>> Tokenize(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text))
            return rows;

        char delimiter = DetectDelimiter(text);
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else if (c is not '\r')
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        int newline = text.IndexOf('\n');
        string header = newline >= 0 ? text[..newline] : text;
        return header.Contains('\t') ? '\t' : ',';
    }
}
