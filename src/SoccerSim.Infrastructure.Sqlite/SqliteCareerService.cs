using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// SQLite-backed <see cref="ICareerService"/>. Resolves the human's identity from the
/// singleton Career row: the controlled team is read from that player's TeamId, and the
/// event-roll trait weights are projected from the player's static traits. Synchronous,
/// connection-only — the same shape as <see cref="SqliteFixtureGateway"/> — so the
/// composition root can load it during Godot's synchronous startup.
/// </summary>
public sealed class SqliteCareerService : ICareerService
{
    private readonly SqliteConnection _connection;

    public SqliteCareerService(SqliteConnection connection) => _connection = connection;

    public CareerState? GetActiveCareer()
    {
        int humanPlayerId;
        CareerRole role;
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT HumanPlayerId, Role FROM Career WHERE Id = 1;";
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
                return null;

            humanPlayerId = reader.GetInt32(0);
            string roleText = reader.GetString(1);
            // Fail fast: an unrecognised role would silently fall back to a Player career and
            // apply the wrong life-sim profile for the rest of the save.
            role = Enum.TryParse(roleText, ignoreCase: false, out CareerRole parsed)
                ? parsed
                : throw new InvalidOperationException($"Career has an unknown role '{roleText}'.");
        }

        int humanTeamId = LoadTeamId(humanPlayerId);
        IReadOnlyDictionary<string, int> traitWeights = PlayerTraitWeights.From(LoadTraits(humanPlayerId));
        return new CareerState(humanPlayerId, humanTeamId, traitWeights, role);
    }

    private int LoadTeamId(int playerId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT TeamId FROM Players WHERE Id = $pid;";
        command.Parameters.AddWithValue("$pid", playerId);
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? throw new InvalidOperationException(
                $"Career references player {playerId}, who has no team to control.")
            : Convert.ToInt32(value);
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
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return traits;
    }
}
