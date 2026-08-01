using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
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
        ulong masterSeed;

        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT HumanPlayerId, MasterSeed FROM Career WHERE Id = 1;";
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            humanPlayerId = reader.GetInt32(0);

            // No seed means no replayable world. Substituting one here would silently generate a
            // different world than the save was written against, so it fails instead.
            if (reader.IsDBNull(1))
            {
                throw new InvalidOperationException(
                    "The active career has no MasterSeed. A save without a seed cannot be replayed; " +
                    "re-run migrations (0006 backfills pre-existing saves) or recreate the career.");
            }

            // SQLite integers are signed 64-bit; the unsigned seed travels as its two's-complement
            // bit pattern, so reinterpreting gives back exactly what was written.
            masterSeed = unchecked((ulong)reader.GetInt64(1));
        }

        int humanTeamId = LoadTeamId(humanPlayerId);
        IReadOnlyDictionary<string, int> traitWeights = PlayerTraitWeights.From(LoadTraits(humanPlayerId));
        return new CareerState(humanPlayerId, humanTeamId, traitWeights, masterSeed);
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
