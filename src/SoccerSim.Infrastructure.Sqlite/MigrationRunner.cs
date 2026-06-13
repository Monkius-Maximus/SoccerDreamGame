using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// Applies the embedded, numbered .sql migrations (from /sql) in order, recording
/// each in a SchemaVersions table so it runs exactly once. Seed files (those whose
/// name contains "seed") are skipped unless explicitly requested.
/// </summary>
public sealed class MigrationRunner
{
    private static readonly Regex VersionPattern = new(@"(\d{4})_", RegexOptions.Compiled);

    private readonly SqliteConnectionFactory _factory;

    public MigrationRunner(SqliteConnectionFactory factory) => _factory = factory;

    public void Migrate(bool includeSeeds = false)
    {
        using SqliteConnection connection = _factory.Open();
        EnsureVersionTable(connection);
        HashSet<int> applied = GetAppliedVersions(connection);

        Assembly assembly = typeof(MigrationRunner).Assembly;
        IEnumerable<string> resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Where(name => includeSeeds || !name.Contains("seed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (string resource in resources)
        {
            int version = ParseVersion(resource);
            if (applied.Contains(version))
                continue;

            string sql = ReadResource(assembly, resource);
            using SqliteTransaction transaction = connection.BeginTransaction();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            using (SqliteCommand record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO SchemaVersions (Version, AppliedAt) VALUES ($v, $t);";
                record.Parameters.AddWithValue("$v", version);
                record.Parameters.AddWithValue("$t", SqliteValue.ToText(DateTime.UtcNow));
                record.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static void EnsureVersionTable(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS SchemaVersions (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }

    private static HashSet<int> GetAppliedVersions(SqliteConnection connection)
    {
        var versions = new HashSet<int>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaVersions;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            versions.Add(reader.GetInt32(0));
        return versions;
    }

    private static int ParseVersion(string resourceName)
    {
        Match match = VersionPattern.Match(resourceName);
        return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
