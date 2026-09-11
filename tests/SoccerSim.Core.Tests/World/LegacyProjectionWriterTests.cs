using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Persistence;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using SoccerSim.Core.World;
using SoccerSim.Core.World.Projection;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests.World;

/// <summary>
/// The other half of Sprint 6: the projection reaches the database the game reads, and a club
/// authored in the tool actually plays a match.
/// </summary>
public sealed class LegacyProjectionWriterTests
{
    /// <summary>Projects the real batch into a fresh migrated database and hands it back.</summary>
    private static async Task<WorldDatabase> ProjectedAsync()
    {
        WorldDatabase database = await WorldDatabase.WithRealWorldImported();
        await ProjectAsync(database);
        return database;
    }

    private static async Task ProjectAsync(WorldDatabase database)
    {
        LegacyWorld legacy = await ReadAndProjectAsync(database);
        using SqliteConnection connection = database.Factory.Open();
        new LegacyProjectionWriter(connection).Write(legacy);
    }

    private static async Task<LegacyWorld> ReadAndProjectAsync(WorldDatabase database)
    {
        await using SqliteWorldUnitOfWork world = database.OpenUnitOfWork();
        return WorldToLegacyProjection.Project(
            await world.Clubs.ListAsync(),
            await world.Characters.ListAsync(),
            await world.Competitions.ListAsync(),
            await world.GeoNodes.ListAsync());
    }

    [Fact]
    public async Task Projecting_FillsTheLegacyTables()
    {
        await using WorldDatabase database = await ProjectedAsync();

        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM Leagues;"));
        Assert.Equal(1, database.Scalar<long>("SELECT COUNT(*) FROM Seasons;"));
        Assert.Equal(20, database.Scalar<long>("SELECT COUNT(*) FROM Teams;"));
        Assert.Equal(688, database.Scalar<long>("SELECT COUNT(*) FROM Players;"));
    }

    [Fact]
    public async Task Projecting_LeavesTheAuthoredWorldAlone()
    {
        await using WorldDatabase database = await ProjectedAsync();

        // The world is the source; projecting reads it and writes somewhere else.
        Assert.Equal(20, database.Scalar<long>("SELECT COUNT(*) FROM Clubs;"));
        Assert.Equal(688, database.Scalar<long>("SELECT COUNT(*) FROM Characters;"));
    }

    [Fact]
    public async Task Projecting_IsIdempotent()
    {
        await using WorldDatabase database = await ProjectedAsync();

        string Rows() => string.Join("|", database.Query(
            "SELECT Id, Name, LeagueId, Budget, EloRating FROM Teams ORDER BY Id;"));

        string before = Rows();
        await ProjectAsync(database);

        Assert.Equal(before, Rows());
        Assert.Equal(20, database.Scalar<long>("SELECT COUNT(*) FROM Teams;"));
        Assert.Equal(688, database.Scalar<long>("SELECT COUNT(*) FROM Players;"));
    }

    [Fact]
    public async Task Projecting_RefusesADatabaseThatHasAlreadyBeenPlayed_AndWritesNothing()
    {
        await using WorldDatabase database = await ProjectedAsync();

        database.Execute(
            @"INSERT INTO Matches (Id, SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played, HomeGoals, AwayGoals)
              VALUES (1, 1, 1, 1, 2, '2026-05-01 16:00:00', 1, 2, 1);");

        long teamsBefore = database.Scalar<long>("SELECT COUNT(*) FROM Teams;");

        var exception = await Assert.ThrowsAsync<ProjectionException>(() => ProjectAsync(database));

        // Ids are positional, so renumbering under a played season would point finished matches
        // at the wrong clubs — a save that looks fine and is wrong.
        Assert.Contains("already holds 1 matches", exception.Message);
        Assert.Equal(teamsBefore, database.Scalar<long>("SELECT COUNT(*) FROM Teams;"));
        Assert.Equal(688, database.Scalar<long>("SELECT COUNT(*) FROM Players;"));
    }

    [Fact]
    public async Task TheProjectedRows_ReadBackThroughTheLegacyRepositories()
    {
        await using WorldDatabase database = await ProjectedAsync();
        await using IUnitOfWork legacy = new SqliteUnitOfWork(database.Factory.Open());

        League league = (await legacy.Leagues.ListAsync()).Single();
        Assert.Equal(SimulationTier.ActiveHuman, league.Tier);

        Season season = (await legacy.Seasons.ListByLeagueAsync(league.Id)).Single();
        Assert.Equal(new DateTime(2026, 1, 1), season.StartDate);
        Assert.Equal(season.Id, league.CurrentSeasonId);

        IReadOnlyList<Team> teams = await legacy.Teams.ListByLeagueAsync(league.Id);
        Assert.Equal(20, teams.Count);

        foreach (Team team in teams)
            Assert.True((await legacy.Players.ListByTeamAsync(team.Id)).Count >= 11);
    }

    /// <summary>
    /// ROADMAP.md Sprint 6, "Pronto quando": a club created in the tool plays a simulated match.
    /// This is that test — the authored world goes through the projection and straight into the
    /// engine that already existed, with nothing in between adapting one to the other.
    /// </summary>
    [Fact]
    public async Task AProjectedClub_PlaysAMatchThroughTheRealEngine()
    {
        await using WorldDatabase database = await ProjectedAsync();
        await using IUnitOfWork legacy = new SqliteUnitOfWork(database.Factory.Open());

        IReadOnlyList<Team> teams = await legacy.Teams.ListAsync();
        Team home = teams[0];
        Team away = teams[1];

        MatchContext context = new(
            new Match
            {
                Id = 1,
                SeasonId = 1,
                LeagueId = home.LeagueId,
                HomeTeamId = home.Id,
                AwayTeamId = away.Id,
                KickoffDate = new DateTime(2026, 5, 1),
            },
            await SnapshotAsync(legacy, home),
            await SnapshotAsync(legacy, away));

        MatchSimulation simulation = new MatchEngine(new SplitMix64Random(2026)).SimulateDetailed(context);

        Assert.InRange(simulation.Result.HomeGoals, 0, 9);
        Assert.InRange(simulation.Result.AwayGoals, 0, 9);
        Assert.Equal(
            simulation.Result.HomeGoals + simulation.Result.AwayGoals,
            simulation.Result.Scorers.Count);
        Assert.Equal(100, simulation.Stats.HomePossession + simulation.Stats.AwayPossession);

        // Every scorer is one of the projected players, not an id the engine invented.
        var squad = simulation.Timeline.Where(moment => moment.PlayerId is not null)
            .Select(moment => moment.PlayerId!.Value)
            .ToHashSet();
        var known = (await legacy.Players.ListAsync()).Select(player => player.Id).ToHashSet();
        Assert.All(squad, id => Assert.Contains(id, known));
    }

    private static async Task<TeamSnapshot> SnapshotAsync(IUnitOfWork legacy, Team team)
    {
        IReadOnlyList<Player> squad = await legacy.Players.ListByTeamAsync(team.Id);

        return new TeamSnapshot(team.Id, team.EloRating, squad.Select(player => player.Id).ToList())
        {
            Players = squad.Select(player => new PlayerSnapshot(player.Id, player.BaseAttributes)).ToList(),
        };
    }
}
