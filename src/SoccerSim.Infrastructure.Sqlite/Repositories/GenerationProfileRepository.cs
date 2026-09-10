using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Generation;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class GenerationProfileRepository : SqliteRepositoryBase, IGenerationProfileRepository
{
    public GenerationProfileRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<GenerationProfiles?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var attributes = new Dictionary<Position, Dictionary<Attr, AttributeProfile>>();
        using (SqliteCommand command = CreateCommand(
            "SELECT Position, Attr, OffsetMean, StdDev FROM GenerationAttributeProfiles;"))
        {
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                var position = WorldRow.Enum<Position>(reader, 0);
                if (!attributes.TryGetValue(position, out Dictionary<Attr, AttributeProfile>? shapes))
                    attributes[position] = shapes = [];

                shapes[WorldRow.Enum<Attr>(reader, 1)] = new AttributeProfile(reader.GetDouble(2), reader.GetDouble(3));
            }
        }

        if (attributes.Count == 0)
            return Task.FromResult<GenerationProfiles?>(null);

        List<string> firstNames = ReadNames("First");
        List<string> lastNames = ReadNames("Last");

        var profiles = new GenerationProfiles(
            attributes.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyDictionary<Attr, AttributeProfile>)entry.Value),
            firstNames,
            lastNames);

        return Task.FromResult<GenerationProfiles?>(profiles);
    }

    public Task SaveAsync(GenerationProfiles profiles, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Execute("DELETE FROM GenerationAttributeProfiles;", bind: null);
        Execute("DELETE FROM GenerationNames;", bind: null);

        foreach ((Position position, IReadOnlyDictionary<Attr, AttributeProfile> shapes) in profiles.Attributes)
        {
            foreach ((Attr attr, AttributeProfile shape) in shapes)
            {
                Execute(
                    @"INSERT INTO GenerationAttributeProfiles (Position, Attr, OffsetMean, StdDev)
                      VALUES ($position, $attr, $mean, $sd);",
                    command =>
                    {
                        command.Parameters.AddWithValue("$position", position.ToString());
                        command.Parameters.AddWithValue("$attr", attr.ToString());
                        command.Parameters.AddWithValue("$mean", shape.OffsetMean);
                        command.Parameters.AddWithValue("$sd", shape.StdDev);
                    });
            }
        }

        WriteNames("First", profiles.FirstNames);
        WriteNames("Last", profiles.LastNames);

        return Task.CompletedTask;
    }

    private List<string> ReadNames(string kind)
    {
        var names = new List<string>();
        using SqliteCommand command = CreateCommand("SELECT Name FROM GenerationNames WHERE Kind = $kind ORDER BY Name;");
        command.Parameters.AddWithValue("$kind", kind);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    private void WriteNames(string kind, IReadOnlyList<string> names)
    {
        // The pools are sets, so a duplicate in the source is not an error — it just does not
        // make that name twice as likely.
        foreach (string name in names.Distinct())
        {
            Execute("INSERT OR IGNORE INTO GenerationNames (Kind, Name) VALUES ($kind, $name);", command =>
            {
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$name", name);
            });
        }
    }

    private void Execute(string sql, Action<SqliteCommand>? bind)
    {
        using SqliteCommand command = CreateCommand(sql);
        bind?.Invoke(command);
        command.ExecuteNonQuery();
    }
}

internal sealed class WorldSettingsRepository : SqliteRepositoryBase, IWorldSettingsRepository
{
    public WorldSettingsRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = new Dictionary<string, string>();
        using SqliteCommand command = CreateCommand("SELECT Key, Value FROM WorldSettings;");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            settings[reader.GetString(0)] = reader.GetString(1);

        return Task.FromResult<IReadOnlyDictionary<string, string>>(settings);
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand("SELECT Value FROM WorldSettings WHERE Key = $key;");
        command.Parameters.AddWithValue("$key", key);
        return Task.FromResult(command.ExecuteScalar() as string);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand(
            @"INSERT INTO WorldSettings (Key, Value) VALUES ($key, $value)
              ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;");
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }
}
