using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Competition definitions with their stages and transition rules (sql/0019). The definition is
/// written whole: an update replaces its stages and rules, so a rule never outlives the version of
/// the competition that stated it.
/// </summary>
internal sealed class CompetitionRepository : SqliteRepositoryBase, ICompetitionRepository
{
    private const string SelectColumns = "CompetitionId, Name, Scope, AnchorGeoNodeId, CountryId, Level, ClubCount";

    public CompetitionRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<Competition?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Competition? shell = null;
        using (SqliteCommand command = CreateCommand(
            $"SELECT {SelectColumns} FROM Competitions WHERE CompetitionId = $id;"))
        {
            command.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
                shell = Map(reader);
        }

        return Task.FromResult(shell is null ? null : Complete(shell));
    }

    public Task<IReadOnlyList<Competition>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var shells = new List<Competition>();
        using (SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Competitions ORDER BY CompetitionId;"))
        {
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                shells.Add(Map(reader));
        }

        return Task.FromResult<IReadOnlyList<Competition>>(shells.Select(Complete).ToList());
    }

    public Task<string> AddAsync(Competition entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO Competitions (CompetitionId, Name, Scope, AnchorGeoNodeId, CountryId, Level, ClubCount)
              VALUES ($id, $name, $scope, $geo, $country, $level, $clubs);"))
        {
            Bind(command, entity);
            command.ExecuteNonQuery();
        }

        WriteChildren(entity);
        return Task.FromResult(entity.CompetitionId);
    }

    public Task UpdateAsync(Competition entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand command = CreateCommand(
            @"UPDATE Competitions SET Name = $name, Scope = $scope, AnchorGeoNodeId = $geo,
                  CountryId = $country, Level = $level, ClubCount = $clubs
              WHERE CompetitionId = $id;"))
        {
            Bind(command, entity);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException($"There is no competition '{entity.CompetitionId}' to update.");
        }

        WriteChildren(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Stages, rules and seasons go with it (ON DELETE CASCADE).
        using SqliteCommand command = CreateCommand("DELETE FROM Competitions WHERE CompetitionId = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private Competition Complete(Competition shell) => shell with
    {
        Stages = LoadStages(shell.CompetitionId),
        Transitions = LoadTransitions(shell.CompetitionId),
    };

    private IReadOnlyList<CompetitionStage> LoadStages(string competitionId)
    {
        var stages = new List<CompetitionStage>();
        using SqliteCommand command = CreateCommand(
            "SELECT Ordinal, Kind, Legs FROM CompetitionStages WHERE CompetitionId = $id ORDER BY Ordinal;");
        command.Parameters.AddWithValue("$id", competitionId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            stages.Add(new CompetitionStage(reader.GetInt32(0), WorldRow.Enum<StageKind>(reader, 1), reader.GetInt32(2)));
        return stages;
    }

    private IReadOnlyList<TransitionRule> LoadTransitions(string competitionId)
    {
        var rules = new List<TransitionRule>();
        using SqliteCommand command = CreateCommand(
            @"SELECT RankFrom, RankTo, TargetCompetitionId FROM TransitionRules
              WHERE CompetitionId = $id ORDER BY RankFrom;");
        command.Parameters.AddWithValue("$id", competitionId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            rules.Add(new TransitionRule(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        return rules;
    }

    private void WriteChildren(Competition competition)
    {
        foreach (string table in new[] { "CompetitionStages", "TransitionRules" })
        {
            using SqliteCommand clear = CreateCommand($"DELETE FROM {table} WHERE CompetitionId = $id;");
            clear.Parameters.AddWithValue("$id", competition.CompetitionId);
            clear.ExecuteNonQuery();
        }

        foreach (CompetitionStage stage in competition.Stages)
        {
            using SqliteCommand command = CreateCommand(
                "INSERT INTO CompetitionStages (CompetitionId, Ordinal, Kind, Legs) VALUES ($id, $ordinal, $kind, $legs);");
            command.Parameters.AddWithValue("$id", competition.CompetitionId);
            command.Parameters.AddWithValue("$ordinal", stage.Ordinal);
            command.Parameters.AddWithValue("$kind", stage.Kind.ToString());
            command.Parameters.AddWithValue("$legs", stage.Legs);
            command.ExecuteNonQuery();
        }

        foreach (TransitionRule rule in competition.Transitions)
        {
            using SqliteCommand command = CreateCommand(
                @"INSERT INTO TransitionRules (CompetitionId, RankFrom, RankTo, TargetCompetitionId)
                  VALUES ($id, $from, $to, $target);");
            command.Parameters.AddWithValue("$id", competition.CompetitionId);
            command.Parameters.AddWithValue("$from", rule.RankFrom);
            command.Parameters.AddWithValue("$to", rule.RankTo);
            command.Parameters.AddWithValue("$target", rule.TargetCompetitionId);
            command.ExecuteNonQuery();
        }
    }

    private static void Bind(SqliteCommand command, Competition entity)
    {
        command.Parameters.AddWithValue("$id", entity.CompetitionId);
        command.Parameters.AddWithValue("$name", entity.Name);
        command.Parameters.AddWithValue("$scope", entity.Scope.ToString());
        command.Parameters.AddWithValue("$geo", entity.AnchorGeoNodeId);
        command.Parameters.AddWithValue("$country", (object?)entity.CountryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$level", (object?)entity.Level ?? DBNull.Value);
        command.Parameters.AddWithValue("$clubs", entity.ClubCount);
    }

    private static Competition Map(SqliteDataReader reader) => new(
        CompetitionId: reader.GetString(0),
        Name: reader.GetString(1),
        Scope: WorldRow.Enum<CompetitionScope>(reader, 2),
        AnchorGeoNodeId: reader.GetString(3),
        CountryId: reader.IsDBNull(4) ? null : reader.GetString(4),
        Level: reader.IsDBNull(5) ? null : reader.GetInt32(5),
        ClubCount: reader.GetInt32(6),
        Stages: [],
        Transitions: []);
}

/// <summary>Seasons and their participants, in authored order (sql/0019).</summary>
internal sealed class CompetitionSeasonRepository : SqliteRepositoryBase, ICompetitionSeasonRepository
{
    public CompetitionSeasonRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<IReadOnlyList<CompetitionSeason>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var shells = new List<CompetitionSeason>();
        using (SqliteCommand command = CreateCommand(
            "SELECT SeasonId, CompetitionId, Year FROM CompetitionSeasons ORDER BY CompetitionId, Year;"))
        {
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                shells.Add(new CompetitionSeason(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), []));
        }

        return Task.FromResult<IReadOnlyList<CompetitionSeason>>(
            shells.Select(season => season with { ParticipantClubIds = LoadParticipants(season.SeasonId) }).ToList());
    }

    public Task SaveAsync(CompetitionSeason season, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO CompetitionSeasons (SeasonId, CompetitionId, Year) VALUES ($id, $competition, $year)
              ON CONFLICT (SeasonId) DO UPDATE SET CompetitionId = excluded.CompetitionId, Year = excluded.Year;"))
        {
            command.Parameters.AddWithValue("$id", season.SeasonId);
            command.Parameters.AddWithValue("$competition", season.CompetitionId);
            command.Parameters.AddWithValue("$year", season.Year);
            command.ExecuteNonQuery();
        }

        using (SqliteCommand clear = CreateCommand("DELETE FROM SeasonParticipants WHERE SeasonId = $id;"))
        {
            clear.Parameters.AddWithValue("$id", season.SeasonId);
            clear.ExecuteNonQuery();
        }

        for (int ordinal = 0; ordinal < season.ParticipantClubIds.Count; ordinal++)
        {
            using SqliteCommand insert = CreateCommand(
                "INSERT INTO SeasonParticipants (SeasonId, ClubId, Ordinal) VALUES ($id, $club, $ordinal);");
            insert.Parameters.AddWithValue("$id", season.SeasonId);
            insert.Parameters.AddWithValue("$club", season.ParticipantClubIds[ordinal]);
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM CompetitionSeasons WHERE SeasonId = $id;");
        command.Parameters.AddWithValue("$id", seasonId);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<string> LoadParticipants(string seasonId)
    {
        var participants = new List<string>();
        using SqliteCommand command = CreateCommand(
            "SELECT ClubId FROM SeasonParticipants WHERE SeasonId = $id ORDER BY Ordinal;");
        command.Parameters.AddWithValue("$id", seasonId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            participants.Add(reader.GetString(0));
        return participants;
    }
}
