using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
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
    public void Tier3_Resolves_WithoutRatings()
    {
        var resolver = new Tier3MathResolver(new SplitMix64Random(7));

        MatchResult result = resolver.Resolve(SampleContext());

        Assert.Equal(1, result.MatchId);
        Assert.InRange(result.HomeGoals, 0, 9);
        Assert.InRange(result.AwayGoals, 0, 9);
        Assert.Equal(result.HomeGoals + result.AwayGoals, result.Scorers.Count);
        Assert.Empty(result.PlayerRatings); // Tier 3 ignores form
    }

    [Fact]
    public void Tier1_And_Tier2_Produce_Ratings()
    {
        MatchContext context = SampleContext();

        Assert.NotEmpty(new Tier1MatchResolver(new SplitMix64Random(1)).Resolve(context).PlayerRatings);
        Assert.NotEmpty(new Tier2EloResolver(new SplitMix64Random(1)).Resolve(context).PlayerRatings);
    }

    [Fact]
    public void Resolver_Tiers_MatchEnum()
    {
        Assert.Equal(SimulationTier.ActiveHuman, new Tier1MatchResolver(new SplitMix64Random(1)).Tier);
        Assert.Equal(SimulationTier.MajorForeign, new Tier2EloResolver(new SplitMix64Random(1)).Tier);
        Assert.Equal(SimulationTier.Minor, new Tier3MathResolver(new SplitMix64Random(1)).Tier);
    }
}
