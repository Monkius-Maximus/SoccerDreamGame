using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class GeoNodeRepository : SqliteRepositoryBase, IGeoNodeRepository
{
    private const string SelectColumns = "GeoNodeId, Kind, ParentId, DisplayName";

    public GeoNodeRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<GeoNode?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM GeoNodes WHERE GeoNodeId = $id;");
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        return Task.FromResult(reader.Read() ? Map(reader) : null);
    }

    public Task<IReadOnlyList<GeoNode>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query($"SELECT {SelectColumns} FROM GeoNodes ORDER BY GeoNodeId;", bind: null));
    }

    public Task<IReadOnlyList<GeoNode>> ListChildrenAsync(string? parentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string predicate = parentId is null ? "ParentId IS NULL" : "ParentId = $pid";
        return Task.FromResult(Query(
            $"SELECT {SelectColumns} FROM GeoNodes WHERE {predicate} ORDER BY DisplayName;",
            command =>
            {
                if (parentId is not null)
                    command.Parameters.AddWithValue("$pid", parentId);
            }));
    }

    public Task<string> AddAsync(GeoNode entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            @"INSERT INTO GeoNodes (GeoNodeId, Kind, ParentId, DisplayName)
              VALUES ($id, $kind, $pid, $name);");
        Bind(command, entity);
        command.ExecuteNonQuery();
        return Task.FromResult(entity.GeoNodeId);
    }

    public Task UpdateAsync(GeoNode entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            "UPDATE GeoNodes SET Kind = $kind, ParentId = $pid, DisplayName = $name WHERE GeoNodeId = $id;");
        Bind(command, entity);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM GeoNodes WHERE GeoNodeId = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<GeoNode> Query(string sql, Action<SqliteCommand>? bind)
    {
        var nodes = new List<GeoNode>();
        using SqliteCommand command = CreateCommand(sql);
        bind?.Invoke(command);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            nodes.Add(Map(reader));
        return nodes;
    }

    private static void Bind(SqliteCommand command, GeoNode entity)
    {
        command.Parameters.AddWithValue("$id", entity.GeoNodeId);
        command.Parameters.AddWithValue("$kind", entity.Kind.ToString());
        command.Parameters.AddWithValue("$pid", WorldRow.OrNull(entity.ParentId));
        command.Parameters.AddWithValue("$name", entity.DisplayName);
    }

    private static GeoNode Map(SqliteDataReader reader) => new(
        reader.GetString(0),
        WorldRow.Enum<GeoNodeKind>(reader, 1),
        WorldRow.NullableString(reader, 2),
        reader.GetString(3));
}
