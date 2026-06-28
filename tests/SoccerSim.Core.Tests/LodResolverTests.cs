using SoccerSim.Core.MatchEngine;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using SoccerSim.Core.Domain;
using Xunit;

namespace SoccerSim.Core.Tests;

public sealed class LodResolverTests
{
    private static MatchContext SampleContext()
    {
        var match = new Match
        {
            Id = 1,
            SeasonId = 1,
            LeagueId = 1,
            HomeTeamId = 1,
            AwayTeamId = 2,
            KickoffDate = new DateTime(2026, 8, 10),
        };
        var home = new TeamSnapshot(1, 1600, new[] { 1, 2, 3 });
        var away = new TeamSnapshot(2, 1500, new[] { 4, 5, 6 });
        return new MatchContext(match, home, away);
    }

    [Fact]
    public void Tier3_Statistical_Resolves_WithoutRatings()
    {
        var resolver = new StatisticalMatchResolver(SimulationTier.Minor, new SplitMix64Random(7));

        MatchResult result = resolver.Resolve(SampleContext());

        Assert.Equal(1, result.MatchId);
        Assert.InRange(result.HomeGoals, 0, 9);
        Assert.InRange(result.AwayGoals, 0, 9);
        Assert.Equal(result.HomeGoals + result.AwayGoals, result.Scorers.Count);
        Assert.Empty(result.PlayerRatings); // Tier 3 ignores form
    }

    [Fact]
    public void Tier2_Statistical_Produces_Ratings()
    {
        var resolver = new StatisticalMatchResolver(SimulationTier.MajorForeign, new SplitMix64Random(1));

        Assert.NotEmpty(resolver.Resolve(SampleContext()).PlayerRatings);
    }

    [Fact]
    public void Statistical_Resolver_Rejects_Tier1()
    {
        // Tier 1 is the tick engine's job — the statistical path must refuse it (no silent fallback).
        Assert.Throws<ArgumentException>(
            () => new StatisticalMatchResolver(SimulationTier.ActiveHuman, new SplitMix64Random(1)));
    }

    [Fact]
    public void Resolver_Tiers_MatchEnum()
    {
        Assert.Equal(SimulationTier.ActiveHuman, new Tier1MatchResolver(new SplitMix64Random(1)).Tier);
        Assert.Equal(SimulationTier.MajorForeign, new StatisticalMatchResolver(SimulationTier.MajorForeign, new SplitMix64Random(1)).Tier);
        Assert.Equal(SimulationTier.Minor, new StatisticalMatchResolver(SimulationTier.Minor, new SplitMix64Random(1)).Tier);
    }
}
