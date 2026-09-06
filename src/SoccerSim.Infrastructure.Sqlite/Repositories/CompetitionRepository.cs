using Microsoft.Data.Sqlite;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.World;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class CompetitionRepository : SqliteRepositoryBase, ICompetitionRepository
{
    private const string SelectColumns =
        @"CompetitionId, Name, Scope, AnchorGeoNodeId, MemberPredicateId, PrestigeBand, LeagueTierFloat,
          Format, ClubCount, Rounds, PromotedIn, RelegatedOut, ContinentalSlots, EditionId, Season";

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

        return Task.FromResult(shell is null ? null : shell with { MemberClubIds = LoadMembers(shell.CompetitionId) });
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

        var competitions = new List<Competition>(shells.Count);
        foreach (Competition shell in shells)
            competitions.Add(shell with { MemberClubIds = LoadMembers(shell.CompetitionId) });
        return Task.FromResult<IReadOnlyList<Competition>>(competitions);
    }

    public Task<string> AddAsync(Competition entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand command = CreateCommand(
            @"INSERT INTO Competitions (CompetitionId, Name, Scope, AnchorGeoNodeId, MemberPredicateId,
                  PrestigeBand, LeagueTierFloat, Format, ClubCount, Rounds, PromotedIn, RelegatedOut,
                  ContinentalSlots, EditionId, Season)
              VALUES ($id, $name, $scope, $geo, $predicate, $band, $tier, $format, $clubs, $rounds,
                  $promoted, $relegated, $slots, $edition, $season);"))
        {
            Bind(command, entity);
            command.ExecuteNonQuery();
        }

        WriteMembers(entity);
        return Task.FromResult(entity.CompetitionId);
    }

    public Task UpdateAsync(Competition entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (SqliteCommand command = CreateCommand(
            @"UPDATE Competitions SET Name = $name, Scope = $scope, AnchorGeoNodeId = $geo,
                  MemberPredicateId = $predicate, PrestigeBand = $band, LeagueTierFloat = $tier,
                  Format = $format, ClubCount = $clubs, Rounds = $rounds, PromotedIn = $promoted,
                  RelegatedOut = $relegated, ContinentalSlots = $slots, EditionId = $edition, Season = $season
              WHERE CompetitionId = $id;"))
        {
            Bind(command, entity);
            command.ExecuteNonQuery();
        }

        WriteMembers(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Competitions WHERE CompetitionId = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<string> LoadMembers(string competitionId)
    {
        var members = new List<string>();
        using SqliteCommand command = CreateCommand(
            "SELECT ClubId FROM CompetitionMembers WHERE CompetitionId = $id ORDER BY Ordinal;");
        command.Parameters.AddWithValue("$id", competitionId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            members.Add(reader.GetString(0));
        return members;
    }

    private void WriteMembers(Competition competition)
    {
        using (SqliteCommand clear = CreateCommand("DELETE FROM CompetitionMembers WHERE CompetitionId = $id;"))
        {
            clear.Parameters.AddWithValue("$id", competition.CompetitionId);
            clear.ExecuteNonQuery();
        }

        for (int i = 0; i < competition.MemberClubIds.Count; i++)
        {
            using SqliteCommand command = CreateCommand(
                "INSERT INTO CompetitionMembers (CompetitionId, ClubId, Ordinal) VALUES ($id, $club, $ordinal);");
            command.Parameters.AddWithValue("$id", competition.CompetitionId);
            command.Parameters.AddWithValue("$club", competition.MemberClubIds[i]);
            command.Parameters.AddWithValue("$ordinal", i);
            command.ExecuteNonQuery();
        }
    }

    private static void Bind(SqliteCommand command, Competition entity)
    {
        command.Parameters.AddWithValue("$id", entity.CompetitionId);
        command.Parameters.AddWithValue("$name", entity.Name);
        command.Parameters.AddWithValue("$scope", entity.Scope.ToString());
        command.Parameters.AddWithValue("$geo", entity.AnchorGeoNodeId);
        command.Parameters.AddWithValue("$predicate", entity.MemberPredicateId);
        command.Parameters.AddWithValue("$band", entity.PrestigeBand.ToString());
        command.Parameters.AddWithValue("$tier", entity.LeagueTierFloat);
        command.Parameters.AddWithValue("$format", entity.Format);
        command.Parameters.AddWithValue("$clubs", entity.ClubCount);
        command.Parameters.AddWithValue("$rounds", entity.Rounds);
        command.Parameters.AddWithValue("$promoted", entity.PromotedIn);
        command.Parameters.AddWithValue("$relegated", entity.RelegatedOut);
        command.Parameters.AddWithValue("$slots", entity.ContinentalSlots);
        command.Parameters.AddWithValue("$edition", entity.EditionId);
        command.Parameters.AddWithValue("$season", entity.Season);
    }

    private static Competition Map(SqliteDataReader reader) => new(
        CompetitionId: reader.GetString(0),
        Name: reader.GetString(1),
        Scope: WorldRow.Enum<CompetitionScope>(reader, 2),
        AnchorGeoNodeId: reader.GetString(3),
        MemberPredicateId: reader.GetString(4),
        PrestigeBand: WorldRow.Enum<PrestigeBand>(reader, 5),
        LeagueTierFloat: reader.GetDouble(6),
        Format: reader.GetString(7),
        ClubCount: reader.GetInt32(8),
        Rounds: reader.GetInt32(9),
        PromotedIn: reader.GetInt32(10),
        RelegatedOut: reader.GetInt32(11),
        ContinentalSlots: reader.GetString(12),
        EditionId: reader.GetString(13),
        Season: reader.GetInt32(14),
        MemberClubIds: []);
}
