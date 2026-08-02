using Microsoft.Data.Sqlite;
using SoccerSim.Content;

namespace SoccerSim.Infrastructure.Sqlite.Content;

/// <summary>Which content build a database was populated from. Singleton row.</summary>
public sealed record ContentBuildRecord(
    string BuildId,
    string ContentHash,
    int FormatVersion,
    int ContentVersion,
    string Generator,
    DateTime ImportedAt);

/// <summary>Reads and writes the <c>ContentBuilds</c> stamp.</summary>
public static class ContentBuildInfo
{
    public static ContentBuildRecord? Read(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT BuildId, ContentHash, FormatVersion, ContentVersion, Generator, ImportedAt "
            + "FROM ContentBuilds WHERE Id = 1;";

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new ContentBuildRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            SqliteValue.ToDate(reader.GetString(5)));
    }

    public static void Write(SqliteConnection connection, SqliteTransaction transaction, ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(manifest);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO ContentBuilds (Id, BuildId, ContentHash, FormatVersion, ContentVersion, "
            + "Generator, ImportedAt) VALUES (1, $build, $hash, $format, $content, $generator, $at);";
        command.Parameters.AddWithValue("$build", manifest.BuildId);
        command.Parameters.AddWithValue("$hash", manifest.ContentHash);
        command.Parameters.AddWithValue("$format", manifest.FormatVersion);
        command.Parameters.AddWithValue("$content", manifest.ContentVersion);
        command.Parameters.AddWithValue("$generator", manifest.Generator);
        command.Parameters.AddWithValue("$at", SqliteValue.ToText(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }
}
