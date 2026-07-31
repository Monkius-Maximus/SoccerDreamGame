using Microsoft.Data.Sqlite;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IWellbeingRepository"/>. Synchronous and connection-only — the same
/// shape as <see cref="SqliteCareerService"/> — so the composition root can load a career's
/// wellbeing during Godot's synchronous startup.
///
/// <para>
/// Need keys round-trip as the <see cref="NeedKind"/> enum name rather than its ordinal, so
/// reordering the enum can never silently reinterpret a saved gauge as a different need.
/// </para>
/// </summary>
public sealed class SqliteWellbeingRepository : IWellbeingRepository
{
    private readonly SqliteConnection _connection;

    public SqliteWellbeingRepository(SqliteConnection connection) => _connection = connection;

    public WellbeingState? Load(int careerId, CareerRole role)
    {
        var values = new Dictionary<NeedKind, double>();

        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT NeedKey, Value FROM CareerWellbeing WHERE CareerId = $cid;";
            command.Parameters.AddWithValue("$cid", careerId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                if (!Enum.TryParse(key, ignoreCase: false, out NeedKind need))
                {
                    throw new InvalidOperationException(
                        $"Career {careerId} has a saved wellbeing row for unknown need '{key}'.");
                }

                values[need] = reader.GetDouble(1);
            }
        }

        // No rows at all means this career has never been advanced — the caller seeds a default
        // state. A PARTIAL set is corruption, and WellbeingState.FromValues fails fast on it.
        return values.Count == 0 ? null : WellbeingState.FromValues(role, values);
    }

    public void Save(int careerId, WellbeingState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        using SqliteTransaction transaction = _connection.BeginTransaction();
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                @"INSERT INTO CareerWellbeing (CareerId, NeedKey, Value) VALUES ($cid, $key, $value)
                  ON CONFLICT (CareerId, NeedKey) DO UPDATE SET Value = excluded.Value;";
            SqliteParameter careerParam = command.Parameters.Add("$cid", SqliteType.Integer);
            SqliteParameter keyParam = command.Parameters.Add("$key", SqliteType.Text);
            SqliteParameter valueParam = command.Parameters.Add("$value", SqliteType.Real);
            careerParam.Value = careerId;

            foreach (KeyValuePair<NeedKind, double> pair in state.ToDictionary())
            {
                keyParam.Value = pair.Key.ToString();
                valueParam.Value = pair.Value;
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public void LogActivity(int careerId, DateTime date, ActivityOutcome outcome)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO LifeActivityLog (CareerId, Date, ActivityKey, Cost)
              VALUES ($cid, $date, $key, $cost);";
        command.Parameters.AddWithValue("$cid", careerId);
        command.Parameters.AddWithValue("$date", SqliteValue.ToText(date));
        command.Parameters.AddWithValue("$key", outcome.ActivityKey);
        command.Parameters.AddWithValue("$cost", outcome.Cost);
        command.ExecuteNonQuery();
    }
}
