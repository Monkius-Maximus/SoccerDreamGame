using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;

namespace SoccerSim.Infrastructure.Sqlite.Repositories;

internal sealed class SeasonRepository : SqliteRepositoryBase, ISeasonRepository
{
    private const string SelectColumns = "Id, LeagueId, StartDate, EndDate";

    public SeasonRepository(SqliteConnection connection, Func<SqliteTransaction?> transactionAccessor)
        : base(connection, transactionAccessor)
    {
    }

    public Task<Season?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand($"SELECT {SelectColumns} FROM Seasons WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        Season? season = reader.Read() ? Map(reader) : null;
        return Task.FromResult(season);
    }

    public Task<IReadOnlyList<Season>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query($"SELECT {SelectColumns} FROM Seasons ORDER BY Id;", bind: null));
    }

    public Task<IReadOnlyList<Season>> ListByLeagueAsync(int leagueId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(
            $"SELECT {SelectColumns} FROM Seasons WHERE LeagueId = $lid ORDER BY Id;",
            command => command.Parameters.AddWithValue("$lid", leagueId)));
    }

    /// <summary>
    /// Inserts, honouring an explicit id when one is given. The projection assigns its own ids so
    /// that <c>Leagues.CurrentSeasonId</c> — a soft pointer with no foreign key — can be written
    /// in the same pass rather than patched up afterwards.
    /// </summary>
    public Task<int> AddAsync(Season entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteCommand command = CreateCommand(entity.Id == 0
            ? @"INSERT INTO Seasons (LeagueId, StartDate, EndDate) VALUES ($lid, $start, $end);
                SELECT last_insert_rowid();"
            : @"INSERT INTO Seasons (Id, LeagueId, StartDate, EndDate) VALUES ($id, $lid, $start, $end);
                SELECT $id;");

        Bind(command, entity);
        if (entity.Id != 0)
            command.Parameters.AddWithValue("$id", entity.Id);

        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    public Task UpdateAsync(Season entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand(
            "UPDATE Seasons SET LeagueId = $lid, StartDate = $start, EndDate = $end WHERE Id = $id;");
        Bind(command, entity);
        command.Parameters.AddWithValue("$id", entity.Id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteCommand command = CreateCommand("DELETE FROM Seasons WHERE Id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private IReadOnlyList<Season> Query(string sql, Action<SqliteCommand>? bind)
    {
        var seasons = new List<Season>();
        using SqliteCommand command = CreateCommand(sql);
        bind?.Invoke(command);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            seasons.Add(Map(reader));
        return seasons;
    }

    private static void Bind(SqliteCommand command, Season entity)
    {
        command.Parameters.AddWithValue("$lid", entity.LeagueId);
        command.Parameters.AddWithValue("$start", SqliteValue.ToText(entity.StartDate));
        command.Parameters.AddWithValue("$end", SqliteValue.ToText(entity.EndDate));
    }

    private static Season Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        LeagueId = reader.GetInt32(1),
        StartDate = SqliteValue.ToDate(reader.GetString(2)),
        EndDate = SqliteValue.ToDate(reader.GetString(3)),
    };
}
