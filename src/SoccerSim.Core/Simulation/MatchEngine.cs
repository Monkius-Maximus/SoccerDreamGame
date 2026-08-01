using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.Numerics;

namespace SoccerSim.Core.Simulation;

/// <summary>Kind of moment recorded on a match timeline; drives the rendered match scene.</summary>
public enum MatchEventKind
{
    KickOff,
    Chance,
    Goal,
    HalfTime,
    FullTime,
}

/// <summary>A single timestamped moment in a simulated match.</summary>
public sealed record MatchMinuteEvent(
    int Minute,
    MatchEventKind Kind,
    int? TeamId,
    int? PlayerId,
    string Description);

/// <summary>Box-score totals for a simulated match. Possession values sum to 100.</summary>
public sealed record MatchStats(
    int HomePossession,
    int AwayPossession,
    int HomeShots,
    int AwayShots,
    int HomeShotsOnTarget,
    int AwayShotsOnTarget);

/// <summary>
/// Full output of the minute-by-minute engine: the persisted <see cref="MatchResult"/>
/// plus the timeline and box score the rendered match scene replays.
/// </summary>
public sealed record MatchSimulation(
    MatchResult Result,
    IReadOnlyList<MatchMinuteEvent> Timeline,
    MatchStats Stats);

/// <summary>Tunable knobs for <see cref="MatchEngine"/>; defaults give realistic top-flight scorelines.</summary>
public sealed record MatchEngineSettings
{
    /// <summary>Regulation length in minutes (the half-time marker lands at the midpoint).</summary>
    public int RegulationMinutes { get; init; } = 90;

    /// <summary>Upper bound on added time; each match draws 1..this many stoppage minutes.</summary>
    public int MaxStoppageMinutes { get; init; } = 5;

    /// <summary>Baseline expected goals for an evenly matched side before the home bump.</summary>
    public double BaseGoals { get; init; } = 1.35;

    /// <summary>Expected-goals bump applied to the home side.</summary>
    public double HomeAdvantageGoals { get; init; } = 0.30;

    /// <summary>Share of shots that are on target.</summary>
    public double OnTargetRate { get; init; } = 0.5;

    /// <summary>Probability an on-target shot beats the keeper.</summary>
    public double GoalConversion { get; init; } = 0.6;

    /// <summary>Hard cap on goals per side, matching the historic resolver bound.</summary>
    public int MaxGoals { get; init; } = 9;

    /// <summary>Blend of player attributes vs. Elo when both are available (0 = pure Elo, 1 = pure attributes).</summary>
    public double AttributeWeight { get; init; } = 0.5;

    public static MatchEngineSettings Default { get; } = new();
}

/// <summary>
/// Tier 1 minute-by-minute match simulation (GDD §6). Each simulated minute either side
/// may carve out a chance; chances become shots, on-target shots, and goals through a
/// short probability pipeline whose rate is anchored to an Elo + attribute expected-goals
/// model. The result is the same <see cref="MatchResult"/> every tier emits, so it drops
/// straight into the LOD pipeline — but it also exposes a minute timeline and box score
/// for the rendered match scene. All randomness flows through <see cref="IRandom"/>, so a
/// fixed seed reproduces a match exactly.
/// </summary>
public sealed class MatchEngine
{
    private const double EloBaseline = 1500.0;

    private readonly IRandom _rng;
    private readonly MatchEngineSettings _settings;

    public MatchEngine(IRandom rng, MatchEngineSettings? settings = null)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _settings = settings ?? MatchEngineSettings.Default;
    }

    /// <summary>Resolve a match to the persisted result contract (used by the Tier 1 resolver).</summary>
    public MatchResult Simulate(MatchContext context) => SimulateDetailed(context).Result;

    /// <summary>Resolve a match and also return the minute timeline + box score for rendering.</summary>
    public MatchSimulation SimulateDetailed(MatchContext context)
    {
        var home = ScoringPool.From(context.Home);
        var away = ScoringPool.From(context.Away);

        double homeElo = EffectiveElo(context.Home);
        double awayElo = EffectiveElo(context.Away);

        // Logistic Elo expectation → expected goals per side, plus the home bump.
        double homeShare = 1.0 / (1.0 + DeterministicMath.Pow10((awayElo - homeElo) / 400.0));
        double xgHome = (_settings.BaseGoals * 2.0 * homeShare) + _settings.HomeAdvantageGoals;
        double xgAway = _settings.BaseGoals * 2.0 * (1.0 - homeShare);

        double conversion = _settings.OnTargetRate * _settings.GoalConversion;
        double homeChancePerMin = ChancePerMinute(xgHome, conversion);
        double awayChancePerMin = ChancePerMinute(xgAway, conversion);

        int totalMinutes = _settings.RegulationMinutes + 1 + _rng.Next(_settings.MaxStoppageMinutes);
        int halfTime = _settings.RegulationMinutes / 2;

        var timeline = new List<MatchMinuteEvent> { new(0, MatchEventKind.KickOff, null, null, "Kick-off") };
        var scorers = new List<ScorerLine>();

        int homeGoals = 0, awayGoals = 0;
        int homeShots = 0, awayShots = 0, homeOnTarget = 0, awayOnTarget = 0;

        for (int minute = 1; minute <= totalMinutes; minute++)
        {
            homeGoals += PlayMinute(context.Home.TeamId, home, homeChancePerMin, minute, homeGoals, timeline, scorers, ref homeShots, ref homeOnTarget);
            awayGoals += PlayMinute(context.Away.TeamId, away, awayChancePerMin, minute, awayGoals, timeline, scorers, ref awayShots, ref awayOnTarget);

            if (minute == halfTime)
                timeline.Add(new MatchMinuteEvent(minute, MatchEventKind.HalfTime, null, null, "Half-time"));
        }

        timeline.Add(new MatchMinuteEvent(totalMinutes, MatchEventKind.FullTime, null, null, "Full-time"));

        var result = new MatchResult(
            context.Match.Id,
            homeGoals,
            awayGoals,
            scorers,
            BuildRatings(context, home, away, homeGoals, awayGoals, scorers));
        var stats = BuildStats(homeElo, awayElo, homeShots, awayShots, homeOnTarget, awayOnTarget);
        return new MatchSimulation(result, timeline, stats);
    }

    /// <summary>Simulate one minute for a single side; returns 1 if it scored, else 0.</summary>
    private int PlayMinute(
        int teamId, ScoringPool pool, double chancePerMinute, int minute, int goalsSoFar,
        List<MatchMinuteEvent> timeline, List<ScorerLine> scorers, ref int shots, ref int shotsOnTarget)
    {
        if (pool.IsEmpty || _rng.NextDouble() >= chancePerMinute)
            return 0;

        shots++;
        if (_rng.NextDouble() >= _settings.OnTargetRate)
        {
            timeline.Add(new MatchMinuteEvent(minute, MatchEventKind.Chance, teamId, null, "Shot off target"));
            return 0;
        }

        shotsOnTarget++;
        bool scored = goalsSoFar < _settings.MaxGoals && _rng.NextDouble() < _settings.GoalConversion;
        if (!scored)
        {
            timeline.Add(new MatchMinuteEvent(minute, MatchEventKind.Chance, teamId, null, "Saved"));
            return 0;
        }

        int scorerId = pool.PickScorer(_rng);
        scorers.Add(new ScorerLine(scorerId, minute));
        timeline.Add(new MatchMinuteEvent(minute, MatchEventKind.Goal, teamId, scorerId, "Goal!"));
        return 1;
    }

    /// <summary>Per-minute chance probability that integrates to <paramref name="xg"/> goals over regulation.</summary>
    private double ChancePerMinute(double xg, double conversion)
    {
        double expectedChances = Math.Max(0.0, xg) / conversion;
        return Math.Clamp(expectedChances / _settings.RegulationMinutes, 0.0, 0.5);
    }

    /// <summary>Fold mean squad quality into the Elo when attributes are present; otherwise pure Elo.</summary>
    private double EffectiveElo(TeamSnapshot team)
    {
        if (team.Players.Count == 0)
            return team.Elo;

        double quality01 = team.Players.Average(p => OverallQuality(p.Attributes));
        double attributeElo = EloBaseline + ((quality01 - 0.5) * 600.0);
        return ((1.0 - _settings.AttributeWeight) * team.Elo) + (_settings.AttributeWeight * attributeElo);
    }

    private IReadOnlyDictionary<int, double> BuildRatings(
        MatchContext context, ScoringPool home, ScoringPool away,
        int homeGoals, int awayGoals, IReadOnlyList<ScorerLine> scorers)
    {
        var goalsByPlayer = new Dictionary<int, int>();
        foreach (ScorerLine scorer in scorers)
            goalsByPlayer[scorer.PlayerId] = goalsByPlayer.GetValueOrDefault(scorer.PlayerId) + 1;

        var ratings = new Dictionary<int, double>();
        RateTeam(ratings, home, ResultBonus(homeGoals, awayGoals), goalsByPlayer);
        RateTeam(ratings, away, ResultBonus(awayGoals, homeGoals), goalsByPlayer);
        return ratings;
    }

    private static void RateTeam(Dictionary<int, double> ratings, ScoringPool pool, double resultBonus, IReadOnlyDictionary<int, int> goalsByPlayer)
    {
        foreach (int playerId in pool.PlayerIds)
        {
            double rating = 6.0 + resultBonus + (goalsByPlayer.GetValueOrDefault(playerId) * 0.8);
            if (pool.TryGetAttributes(playerId, out PlayerAttributes attributes))
                rating += (OverallQuality(attributes) - 0.5) * 0.6;
            ratings[playerId] = Math.Round(Math.Clamp(rating, 1.0, 10.0), 1);
        }
    }

    private static double ResultBonus(int goalsFor, int goalsAgainst)
        => goalsFor > goalsAgainst ? 0.6 : goalsFor == goalsAgainst ? 0.0 : -0.4;

    private static MatchStats BuildStats(double homeElo, double awayElo, int homeShots, int awayShots, int homeOnTarget, int awayOnTarget)
    {
        // Possession tracks relative strength with a small home tilt; purely cosmetic.
        double share = Math.Clamp((homeElo / (homeElo + awayElo)) + 0.03, 0.25, 0.75);
        int homePossession = (int)Math.Round(share * 100.0);
        return new MatchStats(homePossession, 100 - homePossession, homeShots, awayShots, homeOnTarget, awayOnTarget);
    }

    private static double OverallQuality(PlayerAttributes a)
    {
        double mean = (a.Pace + a.Stamina + a.Strength + a.Passing + a.Shooting + a.Tackling + a.Vision) / 7.0;
        return Math.Clamp(mean / 20.0, 0.0, 1.0);
    }

    /// <summary>
    /// The set of players who can score for a side, with shot-share weighting. Built from
    /// per-player attributes when the snapshot carries them (weighting finishers), and from
    /// the bare squad id list (uniform) otherwise — so a team with no players cannot score.
    /// </summary>
    private sealed class ScoringPool
    {
        private readonly IReadOnlyList<int> _ids;
        private readonly double[]? _cumulativeWeights;
        private readonly IReadOnlyDictionary<int, PlayerAttributes>? _attributes;

        private ScoringPool(IReadOnlyList<int> ids, double[]? cumulativeWeights, IReadOnlyDictionary<int, PlayerAttributes>? attributes)
        {
            _ids = ids;
            _cumulativeWeights = cumulativeWeights;
            _attributes = attributes;
        }

        public bool IsEmpty => _ids.Count == 0;

        public IReadOnlyList<int> PlayerIds => _ids;

        public static ScoringPool From(TeamSnapshot team)
        {
            if (team.Players.Count == 0)
                return new ScoringPool(team.SquadPlayerIds, cumulativeWeights: null, attributes: null);

            var ids = new int[team.Players.Count];
            var cumulative = new double[team.Players.Count];
            var attributes = new Dictionary<int, PlayerAttributes>(team.Players.Count);
            double running = 0.0;
            for (int i = 0; i < team.Players.Count; i++)
            {
                PlayerSnapshot player = team.Players[i];
                ids[i] = player.PlayerId;
                attributes[player.PlayerId] = player.Attributes;
                running += AttackWeight(player.Attributes);
                cumulative[i] = running;
            }

            return new ScoringPool(ids, cumulative, attributes);
        }

        public int PickScorer(IRandom rng)
        {
            if (_cumulativeWeights is null)
                return _ids[rng.Next(_ids.Count)];

            double pick = rng.NextDouble() * _cumulativeWeights[^1];
            for (int i = 0; i < _cumulativeWeights.Length; i++)
            {
                if (pick < _cumulativeWeights[i])
                    return _ids[i];
            }

            return _ids[^1];
        }

        public bool TryGetAttributes(int playerId, out PlayerAttributes attributes)
        {
            if (_attributes is not null && _attributes.TryGetValue(playerId, out attributes))
                return true;

            attributes = default;
            return false;
        }

        private static double AttackWeight(PlayerAttributes a) => (a.Shooting * 2.0) + a.Pace + a.Vision + 1.0;
    }
}
