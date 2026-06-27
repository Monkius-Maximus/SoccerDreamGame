using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using SoccerSim.Core.Time;
using SoccerSim.Infrastructure.Sqlite;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class SimulationLodReservationTests
{
    /// <summary>Stub gateway that serves scripted due matches and records which ones get persisted.</summary>
    private sealed class StubFixtureGateway : IFixtureGateway
    {
        private readonly IReadOnlyList<MatchContext> _due;

        public StubFixtureGateway(IReadOnlyList<MatchContext> due) => _due = due;

        public List<int> ResolvedMatchIds { get; } = new();

        public SimulationTier GetTier(int leagueId) => SimulationTier.ActiveHuman;

        public IReadOnlyList<MatchContext> GetDueMatches(DateTime date, SimulationTier tier) =>
            tier == SimulationTier.ActiveHuman ? _due : Array.Empty<MatchContext>();

        public MatchContext? GetMatchContext(int matchId) => null;

        public void SaveResult(MatchContext context, MatchResult result) =>
            ResolvedMatchIds.Add(context.Match.Id);

        public int? GetNextUnplayedMatchId(SimulationTier tier, int? teamId) => null;

        public MatchDisplayInfo? GetMatchDisplayInfo(int matchId) => null;
    }

    private static ILeagueResolver[] Resolvers() => new ILeagueResolver[]
    {
        new Tier1MatchResolver(new SplitMix64Random(1)),
        new Tier2EloResolver(new SplitMix64Random(1)),
        new Tier3MathResolver(new SplitMix64Random(1)),
    };

    private static MatchContext Fixture(int matchId, int homeTeamId, int awayTeamId) => new(
        new Match
        {
            Id = matchId,
            SeasonId = 1,
            LeagueId = 1,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            KickoffDate = new DateTime(2026, 8, 8),
        },
        new TeamSnapshot(homeTeamId, 1500, new[] { (homeTeamId * 10) + 1, (homeTeamId * 10) + 2, (homeTeamId * 10) + 3 }),
        new TeamSnapshot(awayTeamId, 1500, new[] { (awayTeamId * 10) + 1, (awayTeamId * 10) + 2, (awayTeamId * 10) + 3 }));

    [Fact]
    public void OnDayElapsed_Skips_HumanTeamFixtures_ButResolvesOthers()
    {
        var due = new[] { Fixture(matchId: 1, homeTeamId: 1, awayTeamId: 2), Fixture(matchId: 2, homeTeamId: 3, awayTeamId: 4) };
        var gateway = new StubFixtureGateway(due);
        var lod = new SimulationLODManager(gateway, Resolvers(), humanTeamId: 1);

        lod.OnDayElapsed(new DateTime(2026, 8, 8));

        Assert.Equal(new[] { 2 }, gateway.ResolvedMatchIds);   // match 1 (human team) reserved
    }

    [Fact]
    public void OnDayElapsed_ResolvesAll_WhenNoHumanTeamConfigured()
    {
        var due = new[] { Fixture(matchId: 1, homeTeamId: 1, awayTeamId: 2), Fixture(matchId: 2, homeTeamId: 3, awayTeamId: 4) };
        var gateway = new StubFixtureGateway(due);
        var lod = new SimulationLODManager(gateway, Resolvers());   // humanTeamId == null

        lod.OnDayElapsed(new DateTime(2026, 8, 8));

        Assert.Equal(new[] { 1, 2 }, gateway.ResolvedMatchIds);
    }

    [Fact]
    public void CalendarAdvance_DoesNotResolve_HumanTeamFixtures()
    {
        var factory = SqliteConnectionFactory.InMemoryShared($"db-{Guid.NewGuid():N}");
        using SqliteConnection keepAlive = factory.Open();
        new MigrationRunner(factory).Migrate(includeSeeds: true);

        using SqliteConnection connection = factory.Open();
        var gateway = new SqliteFixtureGateway(connection);
        var lod = new SimulationLODManager(gateway, Resolvers(), humanTeamId: 1);
        var time = new TimeManager(
            new GameClock(new DateTime(2026, 8, 1)),
            new EventManager(Array.Empty<EventDefinition>()),   // no events → no interrupts
            lod,
            date => new EventRollContext(1, new Dictionary<string, int>(), 1.0, new SplitMix64Random(0)));

        time.AdvanceCalendar(new DateTime(2026, 8, 20));   // past both seeded Tier 1 fixtures (8/8, 8/15)

        // Both seeded Tier 1 fixtures involve the human team (1), so neither is auto-resolved.
        Assert.Equal(0L, PlayedFlag(connection, 1));
        Assert.Equal(0L, PlayedFlag(connection, 2));
    }

    private static long PlayedFlag(SqliteConnection connection, int matchId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Played FROM Matches WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", matchId);
        return (long)command.ExecuteScalar()!;
    }
}
