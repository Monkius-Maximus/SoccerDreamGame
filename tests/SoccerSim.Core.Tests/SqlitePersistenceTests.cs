using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class SqlitePersistenceTests
{
    // A shared in-memory DB lives only while at least one connection is open, so each
    // test holds a keep-alive connection for the duration.
    private static (SqliteConnectionFactory Factory, SqliteConnection KeepAlive) NewMigratedDb(bool includeSeeds)
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate(includeSeeds);
        return (factory, keepAlive);
    }

    [Fact]
    public void Migrations_Apply_AndSeedLoads()
    {
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(includeSeeds: true);
        using SqliteConnection connection = keepAlive;

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Leagues;";
        Assert.Equal(3L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task Player_RoundTrips_WithStaticTraits()
    {
        (SqliteConnectionFactory factory, SqliteConnection keepAlive) = NewMigratedDb(includeSeeds: true);
        using SqliteConnection _ = keepAlive;

        await using var unitOfWork = new SqliteUnitOfWork(factory.Open());
        Player? player = await unitOfWork.Players.GetAsync(1);

        Assert.NotNull(player);
        Assert.Equal("Alex", player!.FirstName);
        Assert.Equal(2, player.Traits.Count); // seeded with traits 1 and 3
    }

    [Fact]
    public void Lod_ResolvesTier3Match_AndUpdatesStandings()
    {
        (SqliteConnectionFactory _, SqliteConnection keepAlive) = NewMigratedDb(includeSeeds: true);
        using SqliteConnection connection = keepAlive;

        // Schedule a Tier 3 fixture between the two seeded minor-league teams (5 and 6).
        int matchId;
        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.CommandText =
                @"INSERT INTO Matches (SeasonId, LeagueId, HomeTeamId, AwayTeamId, KickoffDate, Played)
                  VALUES (3, 3, 5, 6, '2026-09-05 15:00:00', 0);
                  SELECT last_insert_rowid();";
            matchId = Convert.ToInt32(insert.ExecuteScalar());
        }

        var gateway = new SqliteFixtureGateway(connection);
        var lod = new SimulationLODManager(
            gateway,
            new ILeagueResolver[] { new Tier3MathResolver(RandomStream.Create(3, StreamName.MatchSimulation)) });

        lod.OnWeekElapsed(new DateTime(2026, 9, 7));

        using (SqliteCommand played = connection.CreateCommand())
        {
            played.CommandText = "SELECT Played FROM Matches WHERE Id = $id;";
            played.Parameters.AddWithValue("$id", matchId);
            Assert.Equal(1L, (long)played.ExecuteScalar()!);
        }

        using SqliteCommand standings = connection.CreateCommand();
        standings.CommandText = "SELECT COUNT(*) FROM Standings WHERE SeasonId = 3;";
        Assert.Equal(2L, (long)standings.ExecuteScalar()!);
    }
}
