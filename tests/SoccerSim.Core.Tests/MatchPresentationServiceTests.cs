using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.Simulation;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class MatchPresentationServiceTests
{
    /// <summary>Configurable in-memory <see cref="IFixtureGateway"/> that records SaveResult calls.</summary>
    private sealed class FakeGateway : IFixtureGateway
    {
        public MatchContext? Context { get; init; }
        public int? NextId { get; init; }
        public MatchDisplayInfo? Display { get; init; }
        public int SaveCount { get; private set; }
        public MatchResult? Saved { get; private set; }
        public int? CapturedTeamId { get; private set; }

        public SimulationTier GetTier(int leagueId) => SimulationTier.ActiveHuman;

        public IReadOnlyList<MatchContext> GetDueMatches(DateTime date, SimulationTier tier) =>
            Array.Empty<MatchContext>();

        public MatchContext? GetMatchContext(int matchId) => Context;

        public void SaveResult(MatchContext context, MatchResult result)
        {
            SaveCount++;
            Saved = result;
        }

        public int? GetNextUnplayedMatchId(SimulationTier tier, int? teamId)
        {
            CapturedTeamId = teamId;
            return NextId;
        }

        public MatchDisplayInfo? GetMatchDisplayInfo(int matchId) => Display;
    }

    private static MatchContext SampleContext(bool played) => new(
        new Match
        {
            Id = 1,
            SeasonId = 1,
            LeagueId = 1,
            HomeTeamId = 1,
            AwayTeamId = 2,
            KickoffDate = new DateTime(2026, 8, 8),
            Played = played,
        },
        new TeamSnapshot(1, 1600, new[] { 1, 2, 3 }),
        new TeamSnapshot(2, 1500, new[] { 4, 5, 6 }));

    private static MatchDisplayInfo SampleDisplay() => new(
        1, "Riverside FC", 2, "Hilltop United",
        new Dictionary<int, string> { [1] = "Alex Mercer", [4] = "Sam Doe" });

    [Fact]
    public void Play_Simulates_Persists_Once_AndReturnsDisplay()
    {
        var gateway = new FakeGateway { Context = SampleContext(played: false), Display = SampleDisplay() };
        var service = new MatchPresentationService(gateway, new MatchEngine(new SeededRandom(1)));

        MatchPresentation presentation = service.Play(1);

        Assert.Equal(1, gateway.SaveCount);
        Assert.Same(presentation.Simulation.Result, gateway.Saved);   // persisted exactly what it returned
        Assert.Equal("Riverside FC", presentation.Display.HomeTeamName);
        Assert.NotEmpty(presentation.Simulation.Timeline);
    }

    [Fact]
    public void Play_Throws_AndDoesNotPersist_WhenAlreadyPlayed()
    {
        var gateway = new FakeGateway { Context = SampleContext(played: true), Display = SampleDisplay() };
        var service = new MatchPresentationService(gateway, new MatchEngine(new SeededRandom(1)));

        Assert.Throws<InvalidOperationException>(() => service.Play(1));
        Assert.Equal(0, gateway.SaveCount);
    }

    [Fact]
    public void PlayNextFixture_ReturnsNull_WhenNonePending()
    {
        var gateway = new FakeGateway { NextId = null };
        var service = new MatchPresentationService(gateway, new MatchEngine(new SeededRandom(1)));

        Assert.Null(service.PlayNextFixture(SimulationTier.ActiveHuman));
        Assert.Equal(0, gateway.SaveCount);
    }

    [Fact]
    public void PlayNextFixture_Plays_TheReportedFixture()
    {
        var gateway = new FakeGateway
        {
            NextId = 1,
            Context = SampleContext(played: false),
            Display = SampleDisplay(),
        };
        var service = new MatchPresentationService(gateway, new MatchEngine(new SeededRandom(2)));

        MatchPresentation? presentation = service.PlayNextFixture(SimulationTier.ActiveHuman);

        Assert.NotNull(presentation);
        Assert.Equal(1, gateway.SaveCount);
    }

    [Fact]
    public void PlayNextFixture_EndToEnd_PersistsResult_OnSeededDb()
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        using SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate(includeSeeds: true);

        using SqliteConnection connection = factory.Open();
        var gateway = new SqliteFixtureGateway(connection);
        var service = new MatchPresentationService(gateway, new MatchEngine(new SeededRandom(42)));

        MatchPresentation? presentation = service.PlayNextFixture(SimulationTier.ActiveHuman);

        Assert.NotNull(presentation);
        Assert.Equal("Riverside FC", presentation!.Display.HomeTeamName);
        Assert.Equal("Hilltop United", presentation.Display.AwayTeamName);

        int matchId = presentation.Simulation.Result.MatchId;

        using (SqliteCommand played = connection.CreateCommand())
        {
            played.CommandText = "SELECT Played FROM Matches WHERE Id = $id;";
            played.Parameters.AddWithValue("$id", matchId);
            Assert.Equal(1L, (long)played.ExecuteScalar()!);
        }

        using (SqliteCommand goals = connection.CreateCommand())
        {
            goals.CommandText = "SELECT COUNT(*) FROM Goals WHERE MatchId = $id;";
            goals.Parameters.AddWithValue("$id", matchId);
            Assert.Equal((long)presentation.Simulation.Result.Scorers.Count, (long)goals.ExecuteScalar()!);
        }

        // Both Tier 1 teams now have a season-1 standings row.
        using (SqliteCommand standings = connection.CreateCommand())
        {
            standings.CommandText = "SELECT COUNT(*) FROM Standings WHERE SeasonId = 1;";
            Assert.Equal(2L, (long)standings.ExecuteScalar()!);
        }
    }

    [Fact]
    public void PlayNextFixture_Forwards_HumanTeamId_ToGateway()
    {
        var gateway = new FakeGateway
        {
            NextId = 1,
            Context = SampleContext(played: false),
            Display = SampleDisplay(),
        };
        var service = new MatchPresentationService(gateway, new MatchEngine(new SeededRandom(3)), humanTeamId: 7);

        service.PlayNextFixture(SimulationTier.ActiveHuman);

        Assert.Equal(7, gateway.CapturedTeamId);
    }

    [Fact]
    public void GetNextUnplayedMatchId_FiltersByTeam()
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        using SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate(includeSeeds: true);

        using SqliteConnection connection = factory.Open();
        var gateway = new SqliteFixtureGateway(connection);

        // Seed has two Tier 1 fixtures (ids 1 and 2), both between teams 1 and 2; id 1 is earliest.
        Assert.Equal(1, gateway.GetNextUnplayedMatchId(SimulationTier.ActiveHuman, null));
        Assert.Equal(1, gateway.GetNextUnplayedMatchId(SimulationTier.ActiveHuman, 1));
        Assert.Equal(1, gateway.GetNextUnplayedMatchId(SimulationTier.ActiveHuman, 2));
        Assert.Null(gateway.GetNextUnplayedMatchId(SimulationTier.ActiveHuman, 999));
    }
}
