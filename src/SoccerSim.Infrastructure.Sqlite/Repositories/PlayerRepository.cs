using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class PlayerRepository : SqliteRepositoryBase, IPlayerRepository
{
    private const string SelectColumns =
        "Id, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, Shooting, Tackling, Vision";

    public PlayerRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<Player?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PlayerRow? row = null;
        using (SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Players WHERE Id = $id;"))
        {
            command.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
                row = ReadRow(reader);
        }

        Player? player = row is null ? null : row.Value.ToPlayer(LoadTraits(row.Value.Id));
        return Task.FromResult(player);
    }

    public Task<IReadOnlyList<Player>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueryPlayers($"SELECT {SelectColumns} FROM Players ORDER BY Id;", bind: null));
    }

    public Task<IReadOnlyList<Player>> ListByTeamAsync(int teamId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(QueryPlayers(
            $"SELECT {SelectColumns} FROM Players WHERE TeamId = $tid ORDER BY Id;",
            command => command.Parameters.AddWithValue("$tid", teamId)));
    }

    public Task<int> AddAsync(Player entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int newId;
        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO Players (FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, Shooting, Tackling, Vision)
              VALUES ($fn, $ln, $tid, $pace, $sta, $str, $pas, $sho, $tac, $vis);
              SELECT last_insert_rowid();"))
        {
            BindPlayer(command, entity);
            newId = Convert.ToInt32(command.ExecuteScalar());
        }

        WriteTraitAssignments(newId, entity.Traits);
        return Task.FromResult(newId);
    }

    public Task UpdateAsync(Player entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand(
            @"UPDATE Players SET FirstName = $fn, LastName = $ln, TeamId = $tid,
                Pace = $pace, Stamina = $sta, Strength = $str, Passing = $pas,
                Shooting = $sho, Tackling = $tac, Vision = $vis
              WHERE Id = $id;");
        BindPlayer(command, entity);
        command.Parameters.AddWithValue("$id", entity.Id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand("DELETE FROM Players WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<Player> QueryPlayers(string sql, Action<SqliteCommand>? bind)
    {
        var rows = new List<PlayerRow>();
        using (SqliteCommand command = CreateCommand(sql))
        {
            bind?.Invoke(command);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(ReadRow(reader));
        }

        // The reader is now closed, so the per-player trait queries can reuse the connection.
        var players = new List<Player>(rows.Count);
        foreach (PlayerRow row in rows)
            players.Add(row.ToPlayer(LoadTraits(row.Id)));
        return players;
    }

    private IReadOnlyList<PlayerTrait> LoadTraits(int playerId)
    {
        var traits = new List<PlayerTrait>();
        using SqliteCommand command = CreateCommand(
            @"SELECT t.Id, t.Key, t.DisplayName, t.Aggression, t.Selfishness, t.EventWeightBias
              FROM PlayerTraits t
              JOIN PlayerTraitAssignments a ON a.TraitId = t.Id
              WHERE a.PlayerId = $pid
              ORDER BY t.Id;");
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

    private void WriteTraitAssignments(int playerId, IReadOnlyList<PlayerTrait> traits)
    {
        foreach (PlayerTrait trait in traits)
        {
            using SqliteCommand command = CreateCommand(
                "INSERT OR IGNORE INTO PlayerTraitAssignments (PlayerId, TraitId) VALUES ($pid, $tid);");
            command.Parameters.AddWithValue("$pid", playerId);
            command.Parameters.AddWithValue("$tid", trait.Id);
            command.ExecuteNonQuery();
        }
    }

    private static void BindPlayer(SqliteCommand command, Player entity)
    {
        command.Parameters.AddWithValue("$fn", entity.FirstName);
        command.Parameters.AddWithValue("$ln", entity.LastName);
        command.Parameters.AddWithValue("$tid", (object?)entity.TeamId ?? DBNull.Value);

        PlayerAttributes attributes = entity.BaseAttributes;
        command.Parameters.AddWithValue("$pace", attributes.Pace);
        command.Parameters.AddWithValue("$sta", attributes.Stamina);
        command.Parameters.AddWithValue("$str", attributes.Strength);
        command.Parameters.AddWithValue("$pas", attributes.Passing);
        command.Parameters.AddWithValue("$sho", attributes.Shooting);
        command.Parameters.AddWithValue("$tac", attributes.Tackling);
        command.Parameters.AddWithValue("$vis", attributes.Vision);
    }

    private static PlayerRow ReadRow(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetInt32(3),
        new PlayerAttributes(
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10)));

    private readonly record struct PlayerRow(
        int Id,
        string FirstName,
        string LastName,
        int? TeamId,
        PlayerAttributes Attributes)
    {
        public Player ToPlayer(IReadOnlyList<PlayerTrait> traits) => new()
        {
            Id = Id,
            FirstName = FirstName,
            LastName = LastName,
            TeamId = TeamId,
            BaseAttributes = Attributes,
            Traits = traits,
        };
    }
}
