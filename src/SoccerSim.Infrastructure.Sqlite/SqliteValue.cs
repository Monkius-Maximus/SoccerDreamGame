using System.Globalization;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// Consistent TEXT serialization for values that must round-trip portably between
/// SQLite and a future Postgres/Turso backend (timestamps as ISO-8601 strings).
/// </summary>
internal static class SqliteValue
{
    public const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    public static string ToText(DateTime value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateTime ToDate(string text) =>
        DateTime.ParseExact(text, DateFormat, CultureInfo.InvariantCulture);
}
