using Microsoft.Data.Sqlite;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// Creates open SQLite connections with foreign keys enforced. Centralising this
/// is what makes the storage engine swappable later: a Postgres backend ships a
/// different factory + repositories and nothing in Core or Godot changes.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    static SqliteConnectionFactory()
    {
        // The Microsoft.Data.Sqlite bundle self-registers its native provider, but
        // calling Init() explicitly is harmless and guards against trimming.
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;

    /// <summary>Factory for a file-backed database (the game's user:// save file).</summary>
    public static SqliteConnectionFactory ForFile(string path) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
        }.ToString());

    /// <summary>Factory for a shared in-memory database (used by tests).</summary>
    public static SqliteConnectionFactory InMemoryShared(string name) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = name,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString());

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // SQLite disables FK enforcement per-connection by default.
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }
}
