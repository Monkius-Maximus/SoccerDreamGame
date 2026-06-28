using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class MatchEngineTests
{
    private static Match SampleMatch() => new()
    {
        Id = 42,
        SeasonId = 1,
        LeagueId = 1,
        HomeTeamId = 1,
        AwayTeamId = 2,
        KickoffDate = new DateTime(2026, 8, 10),
    };

    private static MatchContext Context(TeamSnapshot home, TeamSnapshot away) => new(SampleMatch(), home, away);

    private static TeamSnapshot Team(int id, int elo, params int[] squad) => new(id, elo, squad);

    [Fact]
    public void Simulate_IsDeterministic_ForSameSeed()
    {
        MatchContext context = Context(Team(1, 1600, 1, 2, 3), Team(2, 1500, 4, 5, 6));

        MatchResult a = new MatchEngine(new SplitMix64Random(123)).Simulate(context);
        MatchResult b = new MatchEngine(new SplitMix64Random(123)).Simulate(context);

        Assert.Equal(a.HomeGoals, b.HomeGoals);
        Assert.Equal(a.AwayGoals, b.AwayGoals);
        Assert.Equal(a.Scorers, b.Scorers);
        Assert.Equal(a.PlayerRatings, b.PlayerRatings);
    }

    [Fact]
    public void Scorers_Count_Always_Matches_TotalGoals()
    {
        var engine = new MatchEngine(new SplitMix64Random(7));
        MatchContext context = Context(Team(1, 1550, 1, 2, 3), Team(2, 1450, 4, 5, 6));

        for (int i = 0; i < 250; i++)
        {
            MatchResult r = engine.Simulate(context);

            Assert.Equal(r.HomeGoals + r.AwayGoals, r.Scorers.Count);
            Assert.InRange(r.HomeGoals, 0, 9);
            Assert.InRange(r.AwayGoals, 0, 9);
            Assert.All(r.Scorers, s => Assert.InRange(s.Minute, 1, 95));
        }
    }

    [Fact]
    public void Produces_Ratings_For_Every_Squad_Member()
    {
        MatchContext context = Context(Team(1, 1600, 1, 2, 3), Team(2, 1500, 4, 5, 6));

        MatchResult result = new MatchEngine(new SplitMix64Random(1)).Simulate(context);

        Assert.NotEmpty(result.PlayerRatings);
        foreach (int id in new[] { 1, 2, 3, 4, 5, 6 })
            Assert.True(result.PlayerRatings.ContainsKey(id), $"missing rating for player {id}");
        Assert.All(result.PlayerRatings.Values, v => Assert.InRange(v, 1.0, 10.0));
    }

    [Fact]
    public void StrongerTeam_Scores_More_OnAggregate()
    {
        var engine = new MatchEngine(new SplitMix64Random(2024));
        MatchContext context = Context(Team(1, 1850, 1, 2, 3), Team(2, 1300, 4, 5, 6));

        int home = 0, away = 0;
        for (int i = 0; i < 300; i++)
        {
            MatchResult r = engine.Simulate(context);
            home += r.HomeGoals;
            away += r.AwayGoals;
        }

        Assert.True(home > away * 2, $"expected a dominant home tally, got {home} vs {away}");
    }

    [Fact]
    public void Timeline_Brackets_The_Match_And_Goals_Reconcile()
    {
        MatchContext context = Context(Team(1, 1700, 1, 2, 3), Team(2, 1400, 4, 5, 6));

        MatchSimulation sim = new MatchEngine(new SplitMix64Random(99)).SimulateDetailed(context);

        Assert.Equal(1, sim.Timeline.Count(e => e.Kind == MatchEventKind.KickOff));
        Assert.Equal(1, sim.Timeline.Count(e => e.Kind == MatchEventKind.HalfTime));
        Assert.Equal(1, sim.Timeline.Count(e => e.Kind == MatchEventKind.FullTime));

        int goalEvents = sim.Timeline.Count(e => e.Kind == MatchEventKind.Goal);
        Assert.Equal(sim.Result.HomeGoals + sim.Result.AwayGoals, goalEvents);

        Assert.Equal(100, sim.Stats.HomePossession + sim.Stats.AwayPossession);
        Assert.True(sim.Stats.HomeShotsOnTarget <= sim.Stats.HomeShots);
        Assert.True(sim.Stats.AwayShotsOnTarget <= sim.Stats.AwayShots);
    }

    [Fact]
    public void Team_With_No_Players_Cannot_Score()
    {
        // Away squad is empty → only the home side can find the net, and every scorer is a home player.
        MatchContext context = Context(Team(1, 1500, 1, 2, 3), Team(2, 1500));

        var engine = new MatchEngine(new SplitMix64Random(5));
        for (int i = 0; i < 100; i++)
        {
            MatchResult r = engine.Simulate(context);

            Assert.Equal(0, r.AwayGoals);
            Assert.All(r.Scorers, s => Assert.Contains(s.PlayerId, new[] { 1, 2, 3 }));
        }
    }

    [Fact]
    public void Attacking_Attributes_Decide_Who_Scores()
    {
        // Same squad, but player 10 is a 20-shooting striker; the rest can barely shoot.
        var home = new TeamSnapshot(1, 1700, new[] { 10, 11, 12, 13 })
        {
            Players = new[]
            {
                new PlayerSnapshot(10, Attr(shooting: 20)),
                new PlayerSnapshot(11, Attr(shooting: 1)),
                new PlayerSnapshot(12, Attr(shooting: 1)),
                new PlayerSnapshot(13, Attr(shooting: 1)),
            },
        };
        MatchContext context = Context(home, Team(2, 1400, 4, 5, 6));

        var tally = new Dictionary<int, int>();
        var engine = new MatchEngine(new SplitMix64Random(2025));
        for (int i = 0; i < 400; i++)
        {
            foreach (ScorerLine s in engine.Simulate(context).Scorers)
                tally[s.PlayerId] = tally.GetValueOrDefault(s.PlayerId) + 1;
        }

        int strikerGoals = tally.GetValueOrDefault(10);
        Assert.True(strikerGoals > tally.GetValueOrDefault(11), "striker should outscore the scrubs");
        Assert.True(strikerGoals > tally.GetValueOrDefault(12));
        Assert.True(strikerGoals > tally.GetValueOrDefault(13));
    }

    private static PlayerAttributes Attr(int shooting) =>
        new(Pace: 12, Stamina: 12, Strength: 12, Passing: 12, Shooting: shooting, Tackling: 12, Vision: 12);
}
