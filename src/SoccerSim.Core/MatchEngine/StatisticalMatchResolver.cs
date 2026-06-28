using SoccerSim.Core.Events;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.MatchEngine;

/// <summary>
/// Statistical match resolution for Tiers 2 and 3 — a double-Poisson model, no tick loop. It is
/// the cheap path: it never instantiates players on a pitch, only samples a scoreline from two
/// Poisson means derived from team strength, then attributes scorers. Produces the SAME
/// <see cref="MatchResult"/> the Tier 1 tick engine emits, so the orchestration consumes both
/// identically.
/// </summary>
public sealed class StatisticalMatchResolver : ILeagueResolver
{
    // League-average baselines (calibratable). The home/away split plus the multiplier encode
    // home advantage; goals scale with attack vs. the opponent's leakiness.
    private const double LeagueAverageElo = 1500.0;
    private const double LeagueAverageHomeGoals = 1.5;
    private const double LeagueAverageAwayGoals = 1.1;
    private const double HomeAdvantage = 1.15;
    private const double RatingFloor = 0.5;
    private const double RatingCeiling = 2.0;
    private const int MaxGoals = 9; // matches the historic resolver bound

    private readonly IRandom _rng;
    private readonly bool _produceRatings;

    /// <param name="tier">Tier 2 (major foreign) or Tier 3 (minor). Tier 1 uses the tick engine and is rejected.</param>
    /// <param name="rng">Deterministic randomness source (no <see cref="System.Random"/>).</param>
    public StatisticalMatchResolver(SimulationTier tier, IRandom rng)
    {
        if (tier == SimulationTier.ActiveHuman)
            throw new ArgumentException("Tier 1 is resolved by the tick engine, not the statistical resolver.", nameof(tier));

        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        Tier = tier;
        // Tier 2 tracks form weekly (ratings feed it); Tier 3 ignores form/mood entirely.
        _produceRatings = tier == SimulationTier.MajorForeign;
    }

    public SimulationTier Tier { get; }

    public MatchResult Resolve(MatchContext context)
    {
        double attackHome = Rating(context.Home.Elo, attack: true);
        double attackAway = Rating(context.Away.Elo, attack: true);
        double defenceHome = Rating(context.Home.Elo, attack: false);
        double defenceAway = Rating(context.Away.Elo, attack: false);

        // λ_home = Attack_home * Defence_away * leagueHomeGoals * homeAdvantage.
        // TODO Tier 2: multiply each λ by a tactical/form modifier here (one route, no fallback).
        double lambdaHome = attackHome * defenceAway * LeagueAverageHomeGoals * HomeAdvantage;
        double lambdaAway = attackAway * defenceHome * LeagueAverageAwayGoals;

        int homeGoals = Math.Min(MaxGoals, PoissonSampler.Sample(_rng, lambdaHome));
        int awayGoals = Math.Min(MaxGoals, PoissonSampler.Sample(_rng, lambdaAway));

        var scorers = new List<ScorerLine>(homeGoals + awayGoals);
        AppendScorers(scorers, context.Home, homeGoals);
        AppendScorers(scorers, context.Away, awayGoals);

        return new MatchResult(context.Match.Id, homeGoals, awayGoals, scorers, BuildRatings(context, homeGoals, awayGoals));
    }

    public void UpdateForm(MatchContext context, MatchResult result)
    {
        // Tier 2 form is persisted weekly from result.PlayerRatings by the gateway; Tier 3 is a no-op.
    }

    /// <summary>
    /// Strength on a league-relative scale (~0.5..2.0). Attack rises with Elo (scores more);
    /// defence is the inverse (a stronger side concedes fewer, lowering the opponent's λ).
    /// TODO: derive these from squad attribute aggregates instead of Elo once available.
    /// </summary>
    private static double Rating(double elo, bool attack)
    {
        double ratio = attack ? elo / LeagueAverageElo : LeagueAverageElo / elo;
        return Math.Clamp(ratio, RatingFloor, RatingCeiling);
    }

    private void AppendScorers(List<ScorerLine> scorers, TeamSnapshot team, int goals)
    {
        if (team.SquadPlayerIds.Count == 0)
            return; // can't name a scorer with no squad; live data always has one.

        for (int i = 0; i < goals; i++)
        {
            int playerId = team.SquadPlayerIds[_rng.Next(team.SquadPlayerIds.Count)];
            scorers.Add(new ScorerLine(playerId, 1 + _rng.Next(90)));
        }
    }

    private IReadOnlyDictionary<int, double> BuildRatings(MatchContext context, int homeGoals, int awayGoals)
    {
        if (!_produceRatings)
            return new Dictionary<int, double>(); // Tier 3 emits no ratings.

        var ratings = new Dictionary<int, double>();
        RateSquad(ratings, context.Home.SquadPlayerIds, homeGoals, awayGoals);
        RateSquad(ratings, context.Away.SquadPlayerIds, awayGoals, homeGoals);
        return ratings;
    }

    private static void RateSquad(Dictionary<int, double> ratings, IReadOnlyList<int> squad, int goalsFor, int goalsAgainst)
    {
        double rating = Math.Clamp(6.0 + ((goalsFor - goalsAgainst) * 0.3), 1.0, 10.0);
        foreach (int playerId in squad)
            ratings[playerId] = rating;
    }
}
