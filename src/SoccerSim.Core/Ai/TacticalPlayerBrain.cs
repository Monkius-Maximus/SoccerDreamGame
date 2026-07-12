using SoccerSim.Core.Ai.Steering;
using SoccerSim.Core.Ai.Utility;
using SoccerSim.Core.Pitch;
using SoccerSim.Core.Random;
using SoccerSim.Core.Tactics;

namespace SoccerSim.Core.Ai;

/// <summary>
/// The real player brain: ties the movement layer (steering blend anchored to the formation,
/// recomputed EVERY tick) to the decision layer (utility scoring, re-evaluated every
/// <see cref="UtilityReevaluationTicks"/> ticks, only for players who have or contest the ball).
/// Tactics + personality + attributes flow in once through <see cref="BehaviourWeights.Derive"/>;
/// all behaviour differences emerge from those weights.
/// </summary>
public sealed class TacticalPlayerBrain : IPlayerBrain
{
    /// <summary>
    /// Utility re-evaluation cadence. At the simulation's 60 Hz tick rate this is ~7.5
    /// decisions per second — re-scoring every tick is wasted work and causes twitching.
    /// Together with <see cref="UtilityDecider.CurrentActionBonus"/> this is the hysteresis
    /// that keeps near-tied actions stable (a required property, verified by tests).
    /// </summary>
    public const int UtilityReevaluationTicks = 8;

    /// <summary>Within this range a committed tackle becomes an actual tackle attempt.</summary>
    private const double TackleAttemptRadiusMetres = 1.9;

    private const double ArriveSlowingRadiusMetres = 3.0;
    private const double SeparationRadiusMetres = 4.5;

    /// <summary>Carrying/dribbling with the ball is slower than a free run.</summary>
    private const double OnBallSpeedFactor = 0.85;

    private readonly PitchPlayer _player;
    private readonly FormationSlot _slot;
    private readonly BehaviourWeights _weights;
    private readonly IDeterministicRandom _rng;
    private readonly UtilityDecider _decider = new();

    private ActionScore? _committed;
    private int _nextReevaluationTick = int.MinValue;

    public TacticalPlayerBrain(PitchPlayer player, FormationSlot slot, TeamTactics tactics, IDeterministicRandom rng)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _slot = slot ?? throw new ArgumentNullException(nameof(slot));
        ArgumentNullException.ThrowIfNull(tactics);
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _weights = BehaviourWeights.Derive(tactics, player.Personality, slot);
    }

    public int PlayerId => _player.PlayerId;

    /// <summary>The utility action currently committed to, if any (movement-only ticks have none).</summary>
    public PlayerAction? CurrentAction => _committed?.Action;

    /// <summary>How often the utility layer switched to a different action. Low = no twitching.</summary>
    public int DecisionSwitches { get; private set; }

    public Intention Decide(int tick, PlayerPerception perception)
    {
        ArgumentNullException.ThrowIfNull(perception);
        if (perception.Self.PlayerId != _player.PlayerId)
            throw new InvalidOperationException($"Brain of player {_player.PlayerId} received a perception for player {perception.Self.PlayerId}.");

        if (perception.Ball.OwnerPlayerId == _player.PlayerId)
            return DecideWithBall(tick, perception);

        PlayerState? opposingCarrier = FindOpposingCarrier(perception);
        if (opposingCarrier is not null
            && perception.Self.Position.DistanceTo(opposingCarrier.Position) <= _weights.EngageRadiusMetres
            && IsAmongClosestDefenders(perception, opposingCarrier, _weights.SimultaneousPressers))
        {
            return DecideChallenge(tick, perception, opposingCarrier);
        }

        // Off the ball and out of the duel: pure movement, so any stale utility commitment ends.
        DropCommitment(tick);

        if (perception.Ball.IsLoose && IsNearestOfTeamToBall(perception))
        {
            return new Intention.Move(SteeringBehaviors.Pursue(
                perception.Self.Position, perception.Ball.Position, perception.Ball.Velocity, _player.MaxSpeed));
        }

        return new Intention.Move(RepositionVelocity(perception, opposingCarrier));
    }

    /// <summary>Movement layer: blend of arrive-at-anchor, separation, and (out of possession) lane-blocking.</summary>
    private Vec2 RepositionVelocity(PlayerPerception perception, PlayerState? opposingCarrier)
    {
        Vec2 self = perception.Self.Position;
        Vec2 anchor = TacticalAnchor.Compute(_slot, _weights, perception.Self.Side, perception.Ball.Position, perception.Phase, perception.Pitch);

        var components = new List<(Vec2 Velocity, double Weight)>
        {
            (SteeringBehaviors.Arrive(self, anchor, _player.MaxSpeed, ArriveSlowingRadiusMetres), _weights.ArriveWeight),
            (SteeringBehaviors.Separation(self, perception.Teammates.Select(t => t.Position).ToArray(), SeparationRadiusMetres, _player.MaxSpeed), _weights.SeparationWeight),
        };

        if (perception.Phase == MatchPhase.OutOfPossession && opposingCarrier is not null)
        {
            Vec2 ownGoal = perception.Pitch.DefendedGoal(perception.Self.Side);
            components.Add((
                SteeringBehaviors.Interpose(self, opposingCarrier.Position, ownGoal, _player.MaxSpeed),
                _weights.InterposeWeight * 0.35));
        }

        return SteeringBehaviors.Blend(_player.MaxSpeed, components);
    }

    private Intention DecideWithBall(int tick, PlayerPerception perception)
    {
        bool commitmentIsStale = _committed is null || _committed.Action is PlayerAction.Tackle or PlayerAction.Contain;
        if (commitmentIsStale || tick >= _nextReevaluationTick)
        {
            PlayerAction? current = commitmentIsStale ? null : _committed!.Action;
            Commit(tick, _decider.DecideBallCarrier(_player, _weights, perception, current, _rng), countSwitch: !commitmentIsStale);
        }

        ActionScore action = _committed!;
        switch (action.Action)
        {
            case PlayerAction.Shoot:
                DropCommitment(tick); // one-shot: the ball leaves this tick.
                return new Intention.Shoot(action.Target);

            case PlayerAction.ShortPass:
            case PlayerAction.LongPass:
            {
                int targetId = action.TargetPlayerId
                    ?? throw new InvalidOperationException("Pass decision without a target player.");
                PlayerState mate = perception.Teammates.FirstOrDefault(t => t.PlayerId == targetId)
                    ?? throw new InvalidOperationException($"Pass target {targetId} is not a teammate.");
                DropCommitment(tick); // one-shot.
                return new Intention.Pass(targetId, mate.Position + (mate.Velocity * 0.4));
            }

            case PlayerAction.Carry:
            case PlayerAction.Dribble:
                // A reached destination just means standing on the ball until the next scheduled
                // re-evaluation — re-planning immediately would defeat the hysteresis.
                return new Intention.Move(SteeringBehaviors.Arrive(
                    perception.Self.Position, action.Target, _player.MaxSpeed * OnBallSpeedFactor, slowingRadius: 2.0));

            default:
                throw new InvalidOperationException($"Ball carrier committed to off-ball action {action.Action}.");
        }
    }

    private Intention DecideChallenge(int tick, PlayerPerception perception, PlayerState carrier)
    {
        bool commitmentIsStale = _committed is null || _committed.Action is not (PlayerAction.Tackle or PlayerAction.Contain);
        if (commitmentIsStale || tick >= _nextReevaluationTick)
        {
            PlayerAction? current = commitmentIsStale ? null : _committed!.Action;
            Commit(tick, _decider.DecideChallenger(_player, _weights, perception, current, _rng), countSwitch: !commitmentIsStale);
        }

        Vec2 self = perception.Self.Position;
        ActionScore action = _committed!;
        switch (action.Action)
        {
            case PlayerAction.Tackle:
                if (self.DistanceTo(carrier.Position) <= TackleAttemptRadiusMetres)
                    return new Intention.Tackle(carrier.PlayerId);
                return new Intention.Move(SteeringBehaviors.Blend(_player.MaxSpeed, new[]
                {
                    (SteeringBehaviors.Pursue(self, carrier.Position, carrier.Velocity, _player.MaxSpeed), _weights.PursueWeight),
                    (SteeringBehaviors.Separation(self, perception.Teammates.Select(t => t.Position).ToArray(), SeparationRadiusMetres, _player.MaxSpeed), _weights.SeparationWeight),
                }));

            case PlayerAction.Contain:
                return new Intention.Move(SteeringBehaviors.Arrive(self, action.Target, _player.MaxSpeed, ArriveSlowingRadiusMetres));

            default:
                throw new InvalidOperationException($"Challenger committed to on-ball action {action.Action}.");
        }
    }

    /// <summary>
    /// <paramref name="countSwitch"/> is false when the decision CONTEXT changed (won/lost the
    /// ball, entered a duel): that is a legitimate re-plan, not the twitching the oscillation
    /// metric exists to catch.
    /// </summary>
    private void Commit(int tick, ActionScore choice, bool countSwitch)
    {
        if (countSwitch && _committed is not null && _committed.Action != choice.Action)
            DecisionSwitches++;
        _committed = choice;
        _nextReevaluationTick = tick + UtilityReevaluationTicks;
    }

    private void DropCommitment(int tick)
    {
        _committed = null;
        _nextReevaluationTick = tick;
    }

    private static PlayerState? FindOpposingCarrier(PlayerPerception perception)
        => perception.Ball.OwnerPlayerId is int ownerId
            ? perception.Opponents.FirstOrDefault(o => o.PlayerId == ownerId)
            : null;

    /// <summary>Only the closest defenders press; everyone else keeps the shape.</summary>
    private bool IsAmongClosestDefenders(PlayerPerception perception, PlayerState carrier, int count)
    {
        double myDistance = perception.Self.Position.DistanceTo(carrier.Position);
        int closer = perception.Teammates.Count(t =>
        {
            double d = t.Position.DistanceTo(carrier.Position);
            return d < myDistance || (d == myDistance && t.PlayerId < _player.PlayerId);
        });
        return closer < count;
    }

    private bool IsNearestOfTeamToBall(PlayerPerception perception)
    {
        double myDistance = perception.Self.Position.DistanceTo(perception.Ball.Position);
        return !perception.Teammates.Any(t =>
        {
            double d = t.Position.DistanceTo(perception.Ball.Position);
            return d < myDistance || (d == myDistance && t.PlayerId < _player.PlayerId);
        });
    }
}
