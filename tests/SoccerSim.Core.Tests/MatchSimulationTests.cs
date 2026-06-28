using SoccerSim.Core.Domain;
using SoccerSim.Core.MatchEngine;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class MatchSimulationTests
{
    private static readonly PlayerAttributes Attr = new(12, 13, 12, 13, 13, 12, 12);

    private static TeamSnapshot Team(int teamId, int firstPlayerId, int elo)
    {
        var ids = new List<int>();
        var players = new List<PlayerSnapshot>();
        for (int i = 0; i < 11; i++)
        {
            int pid = firstPlayerId + i;
            ids.Add(pid);
            players.Add(new PlayerSnapshot(pid, Attr));
        }

        return new TeamSnapshot(teamId, elo, ids) { Players = players };
    }

    private static MatchSimulation Engine(ulong seed, int regulationSeconds)
        => new(
            matchId: 1,
            home: Team(1, 1, 1600),
            away: Team(2, 12, 1500),
            conditions: MatchConditions.Default,
            rng: new SplitMix64Random(seed),
            brain: new PlaceholderPlayerBrain(),
            referee: new NoOpReferee(),
            settings: new MatchSettings { RegulationSeconds = regulationSeconds, HalfTimeSeconds = Math.Max(1, regulationSeconds / 2) });

    [Fact]
    public void SameSeed_ProducesIdenticalResult()
    {
        MatchResult a = Engine(123, 600).RunToCompletion();
        MatchResult b = Engine(123, 600).RunToCompletion();

        Assert.Equal(a.HomeGoals, b.HomeGoals);
        Assert.Equal(a.AwayGoals, b.AwayGoals);
        Assert.Equal(a.Scorers, b.Scorers);
        Assert.Equal(a.PlayerRatings, b.PlayerRatings);
    }

    [Fact]
    public void SameSeed_ProducesIdenticalBallTrajectory()
    {
        MatchSimulation a = Engine(7, 5400);
        MatchSimulation b = Engine(7, 5400);

        var trailA = new List<Vector3>();
        var trailB = new List<Vector3>();
        for (int i = 0; i < 900; i++)
        {
            a.Step(1.0 / 60);
            b.Step(1.0 / 60);
            trailA.Add(a.Ball.Position);
            trailB.Add(b.Ball.Position);
        }

        Assert.Equal(trailA, trailB);
    }

    [Fact]
    public void RunToCompletion_BracketsTheMatch_AndProducesAValidResult()
    {
        MatchSimulation engine = Engine(99, 600);
        MatchResult result = engine.RunToCompletion();

        Assert.Contains(engine.EventStream, e => e is MatchEvent.KickOff);
        Assert.Contains(engine.EventStream, e => e is MatchEvent.FullTime);
        Assert.True(result.HomeGoals >= 0 && result.AwayGoals >= 0);
        Assert.True(result.Scorers.Count <= result.HomeGoals + result.AwayGoals);
        Assert.All(result.Scorers, s => Assert.InRange(s.Minute, 1, 120));

        MatchBoxScore box = engine.BoxScore();
        Assert.Equal(100, box.HomePossession + box.AwayPossession);
    }

    [Fact]
    public void IncompleteSquad_Throws()
    {
        var thinHome = new TeamSnapshot(1, 1500, new[] { 1, 2, 3 })
        {
            Players = new[] { new PlayerSnapshot(1, Attr), new PlayerSnapshot(2, Attr), new PlayerSnapshot(3, Attr) },
        };

        Assert.Throws<InvalidOperationException>(() => new MatchSimulation(
            1, thinHome, Team(2, 12, 1500), MatchConditions.Default,
            new SplitMix64Random(1), new PlaceholderPlayerBrain(), new NoOpReferee()));
    }

    [Fact]
    public void NullRandom_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MatchSimulation(
            1, Team(1, 1, 1600), Team(2, 12, 1500), MatchConditions.Default,
            null!, new PlaceholderPlayerBrain(), new NoOpReferee()));
    }
}
