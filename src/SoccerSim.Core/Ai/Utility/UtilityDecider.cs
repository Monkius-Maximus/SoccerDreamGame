using SoccerSim.Core.Pitch;
using SoccerSim.Core.Random;

namespace SoccerSim.Core.Ai.Utility;

/// <summary>
/// The decision layer: scores the lean action set for the player who HAS the ball
/// (<see cref="DecideBallCarrier"/>) or is challenging for it (<see cref="DecideChallenger"/>).
/// Every score is a weighted sum of normalised considerations shaped by response curves, then
/// multiplied by the action's tactic/personality bias from <see cref="BehaviourWeights"/>.
/// The decider itself is stateless; hysteresis (commitment + current-action bonus) is applied
/// here via <paramref name="currentAction"/> and owned by the brain.
/// </summary>
public sealed class UtilityDecider
{
    /// <summary>Score gap under which two options count as tied and the deterministic RNG breaks the tie.</summary>
    public const double TieEpsilon = 0.02;

    /// <summary>Hysteresis bonus added to the action already being performed, so near-ties don't twitch.</summary>
    public const double CurrentActionBonus = 0.10;

    private const double ShootingRangeMetres = 25.0;
    private const double ShortPassRangeMetres = 22.0;
    private const double LongPassRangeMetres = 55.0;
    private const double LaneClearFullMetres = 4.0;
    private const double CarryProbeMetres = 8.0;
    private const double DribblePressureRadiusMetres = 6.0;

    /// <summary>Score the on-ball options (shoot / short pass / long pass / carry / dribble) and pick the best.</summary>
    public ActionScore DecideBallCarrier(
        PitchPlayer player,
        BehaviourWeights weights,
        PlayerPerception perception,
        PlayerAction? currentAction,
        IDeterministicRandom rng)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(rng);
        if (perception.Ball.OwnerPlayerId != player.PlayerId)
            throw new InvalidOperationException($"Player {player.PlayerId} is not the ball carrier (owner: {perception.Ball.OwnerPlayerId?.ToString() ?? "loose"}).");

        Vec2 self = perception.Self.Position;
        TeamSide side = perception.Self.Side;
        TeamSide oppSide = side.Opponent();
        Vec2 goal = perception.Pitch.AttackedGoal(side);
        var options = new List<ActionScore>();

        // How hard the carrier is being pressed right now: passes relieve pressure, carries need freedom.
        (PlayerState? nearestOpponent, double nearestDistance) = Nearest(self, perception.Opponents);
        double pressure = nearestOpponent is null
            ? 0.0
            : 1.0 - Math.Clamp(nearestDistance / DribblePressureRadiusMetres, 0.0, 1.0);

        // Shoot: close + central + a finisher's boot.
        double goalDistance = self.DistanceTo(goal);
        double halfWidth = perception.Pitch.Width / 2.0;
        double shootScore = ScoreOf(
            weights.ShootBias,
            new Consideration("goal-proximity", 1.0 - Math.Clamp(goalDistance / ShootingRangeMetres, 0.0, 1.0), ResponseCurve.Linear, 1.3),
            new Consideration("centrality", 1.0 - (Math.Abs(self.Y - halfWidth) / halfWidth), ResponseCurve.Linear, 0.5),
            new Consideration("finishing", player.Finishing, ResponseCurve.Linear, 0.7),
            new Consideration("risk-appetite", weights.RiskTolerance, ResponseCurve.Linear, 0.3));
        // Aim toward the corner away from the shooter's side — deterministic, no draw.
        double aimOffsetY = perception.Pitch.GoalWidth * 0.34 * Math.Sign(halfWidth - self.Y);
        options.Add(new ActionScore(PlayerAction.Shoot, shootScore, null, goal + new Vec2(0.0, aimOffsetY)));

        // Passes: rate every teammate as a target, keep the best short and the best long option.
        PlayerState? bestShort = null;
        PlayerState? bestLong = null;
        double bestShortQuality = 0.0;
        double bestLongQuality = 0.0;
        foreach (PlayerState mate in perception.Teammates)
        {
            double distance = self.DistanceTo(mate.Position);
            if (distance < 2.0 || distance > LongPassRangeMetres)
                continue;

            double lane = LaneClearance(self, mate.Position, perception.Opponents);
            double space = 1.0 - perception.Influence.ControlAt(mate.Position, oppSide);

            // 0.5 = sideways; forward passes rise toward 1, BACKWARD passes sink toward 0 —
            // otherwise recycling toward the (spacious) own half dominates and attacks never build.
            double progress = Math.Clamp(0.5 + ((goalDistance - mate.Position.DistanceTo(goal)) / 40.0), 0.0, 1.0);

            if (distance <= ShortPassRangeMetres)
            {
                double quality = (0.45 * lane) + (0.25 * space) + (0.30 * progress);
                if (quality > bestShortQuality)
                {
                    bestShortQuality = quality;
                    bestShort = mate;
                }
            }
            else
            {
                double quality = (0.30 * lane) + (0.20 * space) + (0.50 * progress);
                if (quality > bestLongQuality)
                {
                    bestLongQuality = quality;
                    bestLong = mate;
                }
            }
        }

        if (bestShort is not null)
        {
            double score = ScoreOf(
                weights.ShortPassBias,
                new Consideration("target-quality", bestShortQuality, ResponseCurve.Linear, 1.2),
                new Consideration("passing", player.PassingSkill, ResponseCurve.Linear, 0.6),
                new Consideration("pressure-relief", pressure, ResponseCurve.Linear, 0.5));
            options.Add(new ActionScore(PlayerAction.ShortPass, score, bestShort.PlayerId, bestShort.Position));
        }

        if (bestLong is not null)
        {
            double score = ScoreOf(
                weights.LongPassBias,
                new Consideration("target-quality", bestLongQuality, ResponseCurve.Linear, 1.2),
                new Consideration("vision", (player.VisionSkill + player.PassingSkill) / 2.0, ResponseCurve.Linear, 0.6),
                new Consideration("risk-appetite", weights.RiskTolerance, ResponseCurve.Linear, 0.4),
                new Consideration("pressure-relief", pressure, ResponseCurve.Linear, 0.4));
            options.Add(new ActionScore(PlayerAction.LongPass, score, bestLong.PlayerId, bestLong.Position));
        }

        // Carry: drive into the space ahead when the map says it is ours and nobody is on top of us.
        Vec2 carryTarget = perception.Pitch.Clamp(self + ((goal - self).Normalized() * CarryProbeMetres));
        double spaceAhead = 1.0 - perception.Influence.ControlAt(carryTarget, oppSide);
        double carryScore = ScoreOf(
            weights.CarryBias,
            new Consideration("space-ahead", spaceAhead, ResponseCurve.Linear, 1.0),
            new Consideration("freedom", 1.0 - pressure, ResponseCurve.Linear, 0.8),
            new Consideration("pace", player.PaceSkill, ResponseCurve.Linear, 0.5));
        options.Add(new ActionScore(PlayerAction.Carry, carryScore, null, carryTarget));

        // Dribble: beat the closest opponent when pressed.
        if (nearestOpponent is not null)
        {
            double dribbleScore = ScoreOf(
                weights.DribbleBias,
                new Consideration("pressure", pressure, ResponseCurve.Linear, 0.9),
                new Consideration("dribbling", player.DribblingSkill, ResponseCurve.Quadratic, 1.0),
                new Consideration("space-beyond", spaceAhead, ResponseCurve.Linear, 0.4));

            // Cut past the marker: forward, offset away from him.
            Vec2 forward = (goal - self).Normalized();
            Vec2 away = (self - nearestOpponent.Position).Normalized();
            Vec2 dribbleTarget = perception.Pitch.Clamp(self + (forward * 6.0) + (away * 3.0));
            options.Add(new ActionScore(PlayerAction.Dribble, dribbleScore, null, dribbleTarget));
        }

        return Choose(options, currentAction, rng);
    }

    /// <summary>Score the off-ball challenge options (tackle vs. contain) against the opposing carrier.</summary>
    public ActionScore DecideChallenger(
        PitchPlayer player,
        BehaviourWeights weights,
        PlayerPerception perception,
        PlayerAction? currentAction,
        IDeterministicRandom rng)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(rng);

        PlayerState carrier = perception.Opponents.FirstOrDefault(o => o.PlayerId == perception.Ball.OwnerPlayerId)
            ?? throw new InvalidOperationException("Challenger decisions require an opposing ball carrier.");

        Vec2 self = perception.Self.Position;
        double distance = self.DistanceTo(carrier.Position);
        double proximity = 1.0 - Math.Clamp(distance / weights.EngageRadiusMetres, 0.0, 1.0);

        double tackleScore = ScoreOf(
            weights.TackleBias,
            new Consideration("proximity", proximity, ResponseCurve.Quadratic, 1.2),
            new Consideration("tackling", player.TacklingSkill, ResponseCurve.Linear, 0.8));

        double containScore = ScoreOf(
            weights.ContainBias,
            new Consideration("hold-position", 0.55, ResponseCurve.Linear, 1.0),
            new Consideration("distance", 1.0 - proximity, ResponseCurve.SquareRoot, 0.5));

        Vec2 ownGoal = perception.Pitch.DefendedGoal(perception.Self.Side);
        Vec2 containSpot = carrier.Position + ((ownGoal - carrier.Position).Normalized() * 2.5);

        var options = new List<ActionScore>
        {
            new(PlayerAction.Tackle, tackleScore, carrier.PlayerId, carrier.Position),
            new(PlayerAction.Contain, containScore, carrier.PlayerId, perception.Pitch.Clamp(containSpot)),
        };
        return Choose(options, currentAction, rng);
    }

    private static (PlayerState? Player, double Distance) Nearest(Vec2 from, IReadOnlyList<PlayerState> players)
    {
        PlayerState? nearest = null;
        double nearestDistance = double.MaxValue;
        foreach (PlayerState player in players)
        {
            double distance = from.DistanceTo(player.Position);
            // Lowest-PlayerId tie-break, matching every other nearest-player helper in the engine
            // so equidistant candidates resolve identically regardless of iteration order.
            if (distance < nearestDistance || (distance == nearestDistance && player.PlayerId < nearest!.PlayerId))
            {
                nearest = player;
                nearestDistance = distance;
            }
        }

        return (nearest, nearestDistance);
    }

    /// <summary>
    /// How clear the from→to passing lane is, in [0, 1]: 1 when no opponent stands near the
    /// segment, 0 when one is right on it. TODO: lofted passes over the first line arrive with
    /// ball-height modelling.
    /// </summary>
    public static double LaneClearance(Vec2 from, Vec2 to, IReadOnlyList<PlayerState> opponents)
    {
        ArgumentNullException.ThrowIfNull(opponents);

        Vec2 segment = to - from;
        double lengthSquared = segment.LengthSquared;
        if (lengthSquared < 1e-9)
            return 1.0;

        double minPerpendicular = double.MaxValue;
        foreach (PlayerState opponent in opponents)
        {
            double t = (opponent.Position - from).Dot(segment) / lengthSquared;
            if (t is < 0.05 or > 0.95)
                continue;

            Vec2 closest = from + (segment * t);
            minPerpendicular = Math.Min(minPerpendicular, opponent.Position.DistanceTo(closest));
        }

        return minPerpendicular == double.MaxValue
            ? 1.0
            : Math.Clamp(minPerpendicular / LaneClearFullMetres, 0.0, 1.0);
    }

    /// <summary>Weighted-average of the considerations (normalised to [0, 1]) times the action bias.</summary>
    private static double ScoreOf(double bias, params Consideration[] considerations)
    {
        double totalScore = 0.0;
        double totalWeight = 0.0;
        foreach (Consideration consideration in considerations)
        {
            totalScore += consideration.Score;
            totalWeight += consideration.Weight;
        }

        return bias * (totalScore / totalWeight);
    }

    private static ActionScore Choose(List<ActionScore> options, PlayerAction? currentAction, IDeterministicRandom rng)
    {
        if (options.Count == 0)
            throw new InvalidOperationException("Utility decision requested with no scorable options.");

        // Hysteresis: the action already underway defends its spot with a flat bonus.
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Action == currentAction)
                options[i] = options[i] with { Score = options[i].Score + CurrentActionBonus };
        }

        double best = options.Max(o => o.Score);
        List<ActionScore> tied = options.Where(o => best - o.Score < TieEpsilon).ToList();
        return tied.Count == 1 ? tied[0] : tied[rng.NextInt(0, tied.Count)];
    }
}
