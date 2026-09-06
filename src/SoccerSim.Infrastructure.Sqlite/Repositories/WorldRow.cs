using Microsoft.Data.Sqlite;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Column reading/binding helpers shared by the world repositories. Enums are stored as TEXT
/// (their C# member name) so the database is readable and its CHECK constraints can name the
/// allowed values; these helpers are the single place that conversion happens.
/// </summary>
internal static class WorldRow
{
    public static TEnum Enum<TEnum>(SqliteDataReader reader, int ordinal)
        where TEnum : struct, System.Enum =>
        System.Enum.Parse<TEnum>(reader.GetString(ordinal));

    public static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static double? NullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    /// <summary>Booleans are INTEGER 0/1 (portable to Postgres BOOLEAN).</summary>
    public static bool Flag(SqliteDataReader reader, int ordinal) => reader.GetInt32(ordinal) != 0;

    /// <summary>Binds a value that may be null, as SQL NULL rather than an empty string.</summary>
    public static object OrNull(object? value) => value ?? DBNull.Value;
}
