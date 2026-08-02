using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IPlayerStateService"/>. Synchronous and connection-only, matching
/// <see cref="SqliteCareerService"/>, so the composition root can load the human's player during
/// Godot's synchronous startup and write their form back after every simulated day.
/// </summary>
public sealed class SqlitePlayerStateService : IPlayerStateService
{
    private readonly SqliteConnection _connection;

    public SqlitePlayerStateService(SqliteConnection connection) => _connection = connection;

    public Player? Load(int playerId)
    {
        Player? player;
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText =
                @"SELECT Id, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing,
                         Shooting, Tackling, Vision
                  FROM Players WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", playerId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            player = new Player
            {
                Id = reader.GetInt32(0),
                FirstName = reader.GetString(1),
                LastName = reader.GetString(2),
                TeamId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                BaseAttributes = new PlayerAttributes(
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                    reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10)),
                Traits = LoadTraits(playerId),
            };
        }

        return player;
    }

    public int? GetCurrentSeasonId(int teamId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            @"SELECT l.CurrentSeasonId
              FROM Teams t JOIN Leagues l ON l.Id = t.LeagueId
              WHERE t.Id = $tid;";
        command.Parameters.AddWithValue("$tid", teamId);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    public void SaveFormMood(int playerId, int seasonId, FormMood form, DateTime date)
    {
        using SqliteCommand command = _connection.CreateCommand();
        // Upsert: form is recalculated every simulated day, not appended. MoodValue is left at its
        // existing value — this service owns form, not mood.
        command.CommandText =
            @"INSERT INTO FormMood (PlayerId, SeasonId, FormValue, MoodValue, LastRecalcDate)
              VALUES ($pid, $sid, $form, 0, $date)
              ON CONFLICT (PlayerId, SeasonId) DO UPDATE SET
                  FormValue = excluded.FormValue,
                  LastRecalcDate = excluded.LastRecalcDate;";
        command.Parameters.AddWithValue("$pid", playerId);
        command.Parameters.AddWithValue("$sid", seasonId);
        command.Parameters.AddWithValue("$form", form.Value);
        command.Parameters.AddWithValue("$date", SqliteValue.ToText(date));
        command.ExecuteNonQuery();
    }

    public FormMood? LoadFormMood(int playerId, int seasonId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT FormValue FROM FormMood WHERE PlayerId = $pid AND SeasonId = $sid;";
        command.Parameters.AddWithValue("$pid", playerId);
        command.Parameters.AddWithValue("$sid", seasonId);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : new FormMood(Convert.ToInt32(value));
    }

    private IReadOnlyList<PlayerTrait> LoadTraits(int playerId)
    {
        var traits = new List<PlayerTrait>();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            @"SELECT t.Id, t.Key, t.DisplayName, t.Aggression, t.Selfishness, t.EventWeightBias
              FROM PlayerTraits t
              JOIN PlayerTraitAssignments a ON a.TraitId = t.Id
              WHERE a.PlayerId = $pid
              ORDER BY t.Id;";
        command.Parameters.AddWithValue("$pid", playerId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            traits.Add(new PlayerTrait(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
        }

        return traits;
    }
}
