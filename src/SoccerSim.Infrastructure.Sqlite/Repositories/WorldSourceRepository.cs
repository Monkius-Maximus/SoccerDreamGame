using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class WorldSourceRepository : SqliteRepositoryBase, IWorldSourceRepository
{
    public WorldSourceRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<IReadOnlyList<WorldSource>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sources = new List<WorldSource>();
        using SqliteCommand command = CreateCommand("SELECT Tema, Numero, Fonte, Url FROM WorldSources ORDER BY Id;");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            sources.Add(new WorldSource(
                reader.GetString(0),
                WorldRow.NullableString(reader, 1),
                WorldRow.NullableString(reader, 2),
                WorldRow.NullableString(reader, 3)));
        }

        return Task.FromResult<IReadOnlyList<WorldSource>>(sources);
    }

    public Task ReplaceAllAsync(IReadOnlyList<WorldSource> sources, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand clear = CreateCommand("DELETE FROM WorldSources;"))
            clear.ExecuteNonQuery();

        // Id is assigned by position so the bibliography reads back in the order it was authored.
        for (int i = 0; i < sources.Count; i++)
        {
            WorldSource source = sources[i];
            using SqliteCommand command = CreateCommand(
                "INSERT INTO WorldSources (Id, Tema, Numero, Fonte, Url) VALUES ($id, $tema, $numero, $fonte, $url);");
            command.Parameters.AddWithValue("$id", i + 1);
            command.Parameters.AddWithValue("$tema", source.Tema);
            command.Parameters.AddWithValue("$numero", WorldRow.OrNull(source.Numero));
            command.Parameters.AddWithValue("$fonte", WorldRow.OrNull(source.Fonte));
            command.Parameters.AddWithValue("$url", WorldRow.OrNull(source.Url));
            command.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }
}
