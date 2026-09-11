namespace SoccerSim.Core.World.Serialization;

public enum ChangeKind { Added, Changed, Removed }

/// <summary>One cell that moves, named by its column.</summary>
public sealed record FieldChange(string Column, string? Before, string? After);

/// <summary>
/// One row that enters, leaves or moves. <see cref="Fields"/> is empty for an addition or a
/// removal — the whole row is the change — and carries only the cells that actually differ for
/// an edit.
/// </summary>
public sealed record WorldChange(
    string Tab,
    string Id,
    ChangeKind Change,
    string Label,
    IReadOnlyList<FieldChange> Fields);

/// <summary>
/// What an import would do, computed on the exported representation of both worlds rather than
/// on the domain records.
///
/// <para>That is the whole trick: the diff can only describe what CSV can express, and it can
/// never drift from the export, because it IS the export. A second field catalogue written for
/// the diff would be a second thing to keep in step with the first.</para>
/// </summary>
public static class WorldCsvDiff
{
    /// <summary>The column that identifies a row, per tab. <c>Fontes</c> has none — it is a list
    /// of citations, not keyed records — so a source is identified by its whole row.</summary>
    private static readonly IReadOnlyDictionary<string, string?> KeyColumns = new Dictionary<string, string?>
    {
        ["Competicao"] = "competitionId",
        ["Clubes"] = "clubId",
        ["Kits_Estadio"] = "clubId",
        ["Jogadores"] = "playerId",
        ["Audit_Clubes"] = "clubId",
        ["Audit_Jogadores"] = "playerId",
        ["GeoNodes"] = "geoNodeId",
        ["Fontes"] = null,
    };

    /// <summary>The column whose value names the row for a human ("Carioca Sul"), so the preview
    /// reads as clubs rather than as identifiers.</summary>
    private static readonly IReadOnlyDictionary<string, string> LabelColumns = new Dictionary<string, string>
    {
        ["Competicao"] = "name",
        ["Clubes"] = "shortName",
        ["Kits_Estadio"] = "shortName",
        ["Jogadores"] = "lastName",
        ["Audit_Clubes"] = "anchorClubName",
        ["Audit_Jogadores"] = "playerId",
        ["GeoNodes"] = "displayName",
        ["Fontes"] = "tema",
    };

    public static IReadOnlyList<WorldChange> Compare(
        WorldSnapshot before,
        WorldSnapshot after,
        IEnumerable<string> tabs)
    {
        var changes = new List<WorldChange>();

        foreach (string name in tabs)
        {
            CsvTab tab = WorldCsv.Tab(name);
            changes.AddRange(CompareTab(tab, before, after));
        }

        return changes;
    }

    private static IEnumerable<WorldChange> CompareTab(CsvTab tab, WorldSnapshot before, WorldSnapshot after)
    {
        string? keyColumn = KeyColumns[tab.Name];
        int labelIndex = tab.Columns.ToList().IndexOf(LabelColumns[tab.Name]);

        Dictionary<string, IReadOnlyList<string?>> oldRows = Key(tab, before, keyColumn);
        Dictionary<string, IReadOnlyList<string?>> newRows = Key(tab, after, keyColumn);

        foreach ((string id, IReadOnlyList<string?> row) in newRows)
        {
            if (!oldRows.TryGetValue(id, out IReadOnlyList<string?>? previous))
            {
                yield return new WorldChange(tab.Name, id, ChangeKind.Added, Label(row, labelIndex), []);
                continue;
            }

            var fields = new List<FieldChange>();
            for (int i = 0; i < tab.Columns.Count; i++)
            {
                string? left = Normalize(previous.ElementAtOrDefault(i));
                string? right = Normalize(row.ElementAtOrDefault(i));

                if (left != right)
                    fields.Add(new FieldChange(tab.Columns[i], left, right));
            }

            if (fields.Count > 0)
                yield return new WorldChange(tab.Name, id, ChangeKind.Changed, Label(row, labelIndex), fields);
        }

        foreach ((string id, IReadOnlyList<string?> row) in oldRows)
        {
            if (!newRows.ContainsKey(id))
                yield return new WorldChange(tab.Name, id, ChangeKind.Removed, Label(row, labelIndex), []);
        }
    }

    private static Dictionary<string, IReadOnlyList<string?>> Key(CsvTab tab, WorldSnapshot world, string? keyColumn)
    {
        int index = keyColumn is null ? -1 : tab.Columns.ToList().IndexOf(keyColumn);
        var keyed = new Dictionary<string, IReadOnlyList<string?>>();

        foreach (IReadOnlyList<string?> row in tab.Rows(world))
        {
            // A keyless tab (Fontes) is identified by its content; a duplicate row is then the
            // same row, which is the right answer for a list of citations.
            string key = index < 0 ? Csv.Write([], [row]) : row[index] ?? string.Empty;
            keyed[key] = row;
        }

        return keyed;
    }

    private static string Label(IReadOnlyList<string?> row, int index) =>
        (index >= 0 ? row.ElementAtOrDefault(index) : null) ?? string.Empty;

    /// <summary>An empty cell and an absent one are the same absence; anything else would make the
    /// preview show changes that are not there.</summary>
    private static string? Normalize(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
