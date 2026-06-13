using SoccerSim.Core.Events;

namespace SoccerSim.Core.Simulation;

/// <summary>
/// Shared Elo→scoreline math used by every tier. Keeps the three concrete
/// resolvers tiny: they differ only in detail level and how they touch form.
/// </summary>
public abstract class EloResolverBase : ILeagueResolver
{
    private const double HomeAdvantageGoals = 0.35;
    private const int MaxGoals = 9;

    protected EloResolverBase(IRandom rng) => Rng = rng;

    protected IRandom Rng { get; }

    public abstract SimulationTier Tier { get; }

    public virtual MatchResult Resolve(MatchContext context)
    {
        // Logistic Elo expectation → expected goals for each side.
        double expectedHome = 1.0 / (1.0 + Math.Pow(10, (context.Away.Elo - context.Home.Elo) / 400.0));

        int homeGoals = SampleGoals((expectedHome * 3.0) + HomeAdvantageGoals);
        int awayGoals = SampleGoals((1.0 - expectedHome) * 3.0);

        var scorers = new List<ScorerLine>(homeGoals + awayGoals);
        AppendScorers(scorers, context.Home, homeGoals);
        AppendScorers(scorers, context.Away, awayGoals);

        return new MatchResult(
            context.Match.Id,
            homeGoals,
            awayGoals,
            scorers,
            BuildRatings(context, homeGoals, awayGoals));
    }

    public abstract void UpdateForm(MatchContext context, MatchResult result);

    /// <summary>Tier 1/2 produce per-player ratings (which drive form); Tier 3 returns none.</summary>
    protected virtual IReadOnlyDictionary<int, double> BuildRatings(MatchContext context, int homeGoals, int awayGoals)
        => new Dictionary<int, double>();

    private int SampleGoals(double expectation)
    {
        double lambda = Math.Max(0.0, expectation);
        int whole = (int)Math.Floor(lambda);
        if (Rng.NextDouble() < lambda - whole)
            whole++;
        return Math.Clamp(whole, 0, MaxGoals);
    }

    private void AppendScorers(List<ScorerLine> scorers, TeamSnapshot team, int goals)
    {
        if (team.SquadPlayerIds.Count == 0)
            return;

        for (int i = 0; i < goals; i++)
        {
            int playerId = team.SquadPlayerIds[Rng.Next(team.SquadPlayerIds.Count)];
            scorers.Add(new ScorerLine(playerId, 1 + Rng.Next(90)));
        }
    }
}

/// <summary>
/// Tier 1 (active human league): full detail. In production this delegates to the
/// minute-by-minute match engine; the scaffold uses the shared Elo math as the
/// placeholder engine while still emitting per-player ratings and tracking form daily.
/// </summary>
public sealed class Tier1MatchResolver : EloResolverBase
{
    public Tier1MatchResolver(IRandom rng) : base(rng) { }

    public override SimulationTier Tier => SimulationTier.ActiveHuman;

    protected override IReadOnlyDictionary<int, double> BuildRatings(MatchContext context, int homeGoals, int awayGoals)
        => RatingHelper.RateSquads(context, homeGoals, awayGoals);

    public override void UpdateForm(MatchContext context, MatchResult result)
    {
        // Tier 1 tracks form daily; the gateway persists it from result.PlayerRatings.
    }
}

/// <summary>Tier 2 (major foreign): Elo aggregate; form updated weekly from match rating.</summary>
public sealed class Tier2EloResolver : EloResolverBase
{
    public Tier2EloResolver(IRandom rng) : base(rng) { }

    public override SimulationTier Tier => SimulationTier.MajorForeign;

    protected override IReadOnlyDictionary<int, double> BuildRatings(MatchContext context, int homeGoals, int awayGoals)
        => RatingHelper.RateSquads(context, homeGoals, awayGoals);

    public override void UpdateForm(MatchContext context, MatchResult result)
    {
        // Weekly cadence; the gateway persists form from result.PlayerRatings on OnWeekElapsed.
    }
}

/// <summary>Tier 3 (minor leagues): pure math at week's end. Form ignored; base attributes only.</summary>
public sealed class Tier3MathResolver : EloResolverBase
{
    public Tier3MathResolver(IRandom rng) : base(rng) { }

    public override SimulationTier Tier => SimulationTier.Minor;

    // BuildRatings intentionally left as the empty base — Tier 3 ignores form/mood.

    public override void UpdateForm(MatchContext context, MatchResult result)
    {
        // No-op: minor leagues perform at base attributes and ignore form/mood.
    }
}

/// <summary>Derives simple match ratings from the scoreline, shared by Tier 1/2.</summary>
internal static class RatingHelper
{
    public static IReadOnlyDictionary<int, double> RateSquads(MatchContext context, int homeGoals, int awayGoals)
    {
        var ratings = new Dictionary<int, double>();
        AddTeam(ratings, context.Home.SquadPlayerIds, homeGoals, awayGoals);
        AddTeam(ratings, context.Away.SquadPlayerIds, awayGoals, homeGoals);
        return ratings;
    }

    private static void AddTeam(Dictionary<int, double> ratings, IReadOnlyList<int> squad, int goalsFor, int goalsAgainst)
    {
        double rating = Math.Clamp(6.0 + (goalsFor - goalsAgainst) * 0.3, 1.0, 10.0);
        foreach (int playerId in squad)
            ratings[playerId] = rating;
    }
}
