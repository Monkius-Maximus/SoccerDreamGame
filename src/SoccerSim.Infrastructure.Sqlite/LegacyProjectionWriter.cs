using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.World.Projection;

namespace SoccerSim.Infrastructure.Sqlite;

/// <summary>
/// Writes a <see cref="LegacyWorld"/> over the legacy tables, replacing whatever was there
/// (ROADMAP.md Sprint 6: <c>worldbuilder project</c> "regrava as tabelas legadas a partir do
/// mundo"). One transaction: either the whole playable world lands or none of it does.
///
/// <para>This lives in the SQLite project and speaks SQL directly, which is the point — the
/// rewrite is a bulk operation over four tables with a foreign-key order, and expressing it as
/// several hundred repository calls would describe it less clearly and run no better.</para>
/// </summary>
public sealed class LegacyProjectionWriter
{
    private readonly SqliteConnection _connection;

    public LegacyProjectionWriter(SqliteConnection connection) => _connection = connection;

    /// <summary>Writes the projection. Throws <see cref="ProjectionException"/> without touching
    /// anything if the database has already been played.</summary>
    public void Write(LegacyWorld world)
    {
        GuardAgainstOverwritingAPlayedWorld();

        using SqliteTransaction transaction = _connection.BeginTransaction();

        // Child before parent. Players first because Teams.Id is what they point at; then
        // Leagues, whose cascade takes Teams and Seasons with it.
        Execute(transaction, "DELETE FROM Players;");
        Execute(transaction, "DELETE FROM Leagues;");

        foreach (League league in world.Leagues)
            InsertLeague(transaction, league);

        foreach (Season season in world.Seasons)
            InsertSeason(transaction, season);

        foreach (Team team in world.Teams)
            InsertTeam(transaction, team);

        foreach (Player player in world.Players)
            InsertPlayer(transaction, player);

        transaction.Commit();
    }

    /// <summary>
    /// Projecting assigns ids by position in the world, so a world that gains or loses a club
    /// renumbers every team after it. Doing that under a career in progress would silently point
    /// finished matches at the wrong clubs — a save that looks fine and is wrong. Refuse instead,
    /// and say what to do about it.
    /// </summary>
    private void GuardAgainstOverwritingAPlayedWorld()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Matches;";
        long matches = Convert.ToInt64(command.ExecuteScalar());

        if (matches == 0)
            return;

        throw new ProjectionException(
        [
            $"this database already holds {matches} matches. Projecting renumbers every team, which "
            + "would leave those matches pointing at the wrong clubs. Project into a new database file, "
            + "or clear the played season first.",
        ]);
    }

    private static void Execute(SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteTransaction transaction, string sql)
    {
        SqliteCommand command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void InsertLeague(SqliteTransaction transaction, League league)
    {
        using SqliteCommand command = Command(transaction,
            @"INSERT INTO Leagues (Id, Name, Country, Tier, CurrentSeasonId)
              VALUES ($id, $name, $country, $tier, $season);");

        command.Parameters.AddWithValue("$id", league.Id);
        command.Parameters.AddWithValue("$name", league.Name);
        command.Parameters.AddWithValue("$country", league.Country);
        command.Parameters.AddWithValue("$tier", (int)league.Tier);
        command.Parameters.AddWithValue("$season", (object?)league.CurrentSeasonId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertSeason(SqliteTransaction transaction, Season season)
    {
        using SqliteCommand command = Command(transaction,
            "INSERT INTO Seasons (Id, LeagueId, StartDate, EndDate) VALUES ($id, $lid, $start, $end);");

        command.Parameters.AddWithValue("$id", season.Id);
        command.Parameters.AddWithValue("$lid", season.LeagueId);
        command.Parameters.AddWithValue("$start", SqliteValue.ToText(season.StartDate));
        command.Parameters.AddWithValue("$end", SqliteValue.ToText(season.EndDate));
        command.ExecuteNonQuery();
    }

    private static void InsertTeam(SqliteTransaction transaction, Team team)
    {
        using SqliteCommand command = Command(transaction,
            "INSERT INTO Teams (Id, Name, LeagueId, Budget, EloRating) VALUES ($id, $name, $lid, $budget, $elo);");

        command.Parameters.AddWithValue("$id", team.Id);
        command.Parameters.AddWithValue("$name", team.Name);
        command.Parameters.AddWithValue("$lid", team.LeagueId);
        command.Parameters.AddWithValue("$budget", team.Budget);
        command.Parameters.AddWithValue("$elo", team.EloRating);
        command.ExecuteNonQuery();
    }

    private static void InsertPlayer(SqliteTransaction transaction, Player player)
    {
        using SqliteCommand command = Command(transaction,
            @"INSERT INTO Players (Id, FirstName, LastName, TeamId, Pace, Stamina, Strength, Passing, Shooting, Tackling, Vision)
              VALUES ($id, $first, $last, $team, $pace, $stamina, $strength, $passing, $shooting, $tackling, $vision);");

        PlayerAttributes attributes = player.BaseAttributes;

        command.Parameters.AddWithValue("$id", player.Id);
        command.Parameters.AddWithValue("$first", player.FirstName);
        command.Parameters.AddWithValue("$last", player.LastName);
        command.Parameters.AddWithValue("$team", (object?)player.TeamId ?? DBNull.Value);
        command.Parameters.AddWithValue("$pace", attributes.Pace);
        command.Parameters.AddWithValue("$stamina", attributes.Stamina);
        command.Parameters.AddWithValue("$strength", attributes.Strength);
        command.Parameters.AddWithValue("$passing", attributes.Passing);
        command.Parameters.AddWithValue("$shooting", attributes.Shooting);
        command.Parameters.AddWithValue("$tackling", attributes.Tackling);
        command.Parameters.AddWithValue("$vision", attributes.Vision);
        command.ExecuteNonQuery();
    }
}
