using SoccerSim.Core.Ai;
using SoccerSim.Core.Random;
using SoccerSim.Core.Tactics;

namespace SoccerSim.Core.Pitch;

/// <summary>One side's setup: eleven players assigned to the tactic's formation slots by index.</summary>
public sealed record PitchTeam(IReadOnlyList<PitchPlayer> Players, TeamTactics Tactics);

/// <summary>Things that happened on the pitch, for tests, logging, and the future rendered scene.</summary>
public enum PitchEventKind
{
    KickOff,
    PassStarted,
    PassCompleted,
    Interception,
    BallRecovered,
    Shot,
    Save,
    Goal,
    TackleAttempt,
    TackleWon,
}

/// <summary>A pitch event. <see cref="Value"/> carries the event's magnitude (pass/shot distance in metres).</summary>
public sealed record PitchEvent(int Tick, PitchEventKind Kind, int PlayerId, int? OtherPlayerId, double Value);

/// <summary>
/// The tick-based Tier 1 on-pitch simulation: the host that owns player/ball state and drives
/// one <see cref="IPlayerBrain"/> per player (~22 brains — LOD guarantees this never runs for
/// the whole world). It is deliberately MINIMAL: it exists to execute intentions
/// deterministically; match realism lives in the brains, not here.
///
/// This constructor is the brain injection seam the AI module plugs into: it wires
/// <see cref="TacticalPlayerBrain"/> as THE brain implementation (this repo never shipped a
/// placeholder brain, so there was nothing to retire).
///
/// The minute-by-minute <c>MatchEngine</c> remains the LOD Tier 1 resolver for background
/// simulation; this tick simulation is the substrate for the rendered/playable match scene.
/// </summary>
public sealed class PitchSimulation
{
    public const int TicksPerSecond = 60;
    public const double TickSeconds = 1.0 / TicksPerSecond;

    private const double ControlRadiusMetres = 1.3;
    private const double TackleResolveRadiusMetres = 2.0;

    /// <summary>
    /// Exponential ball drag. With v' = −k·v the ball loses exactly k m/s per metre travelled,
    /// so a pass launched at k·d + arrivalSpeed reaches its target d metres away still rolling.
    /// </summary>
    private const double BallFrictionPerSecond = 1.0;

    /// <summary>A struck shot flies rather than rolls; far less drag until it is dead or saved.</summary>
    private const double ShotFrictionPerSecond = 0.25;
    private const double PassArrivalSpeedMetresPerSecond = 10.0;

    /// <summary>
    /// A moving ball can only be brought under control below this speed — except by the pass's
    /// intended receiver, who is set to kill it. Keeps markers from stealing passes at the
    /// passer's feet on the release tick.
    /// </summary>
    private const double ControllableSpeedMetresPerSecond = 14.0;
    private const double MaxBallSpeedMetresPerSecond = 34.0;
    private const double ShotSpeedMetresPerSecond = 26.0;
    private const int TackleCooldownTicks = 45;
    private const int PasserPickupLockTicks = 12;
    private const double GoalkeeperReachMetres = 3.0;

    /// <summary>
    /// Ticks a new owner needs to bring the ball under control before he can release it again
    /// (first touch + backswing). This is the pressing window: without it every carrier plays
    /// one-touch and a challenger can never reach tackling range.
    /// </summary>
    private const int FirstTouchTicks = 20;

    private readonly PitchDimensions _pitch;
    private readonly IDeterministicRandom _rng;
    private readonly InfluenceMap _influence;
    private readonly List<Runtime> _all = new();
    private readonly List<PitchEvent> _events = new();

    private (int FromPlayerId, int ToPlayerId, int ReleaseTick, Vec2 Origin)? _pendingPass;
    private (int ShooterId, double Finishing)? _shotInFlight;
    private int _ownerControlReadyTick;

    public PitchSimulation(PitchTeam home, PitchTeam away, IDeterministicRandom rng, PitchDimensions? pitch = null)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(away);
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _pitch = pitch ?? PitchDimensions.Standard;
        _influence = InfluenceMap.CreateDefault(_pitch);

        BuildSide(home, TeamSide.Home);
        BuildSide(away, TeamSide.Away);

        var duplicateIds = _all.GroupBy(r => r.Player.PlayerId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Count > 0)
            throw new ArgumentException($"Player ids must be unique across both teams; duplicated: {string.Join(", ", duplicateIds)}.");

        Runtime kickOffTaker = NearestRuntime(_pitch.Centre, r => r.Side == TeamSide.Home);
        Ball = new BallState(kickOffTaker.Position, Vec2.Zero, kickOffTaker.Player.PlayerId);
        _ownerControlReadyTick = FirstTouchTicks;
        _events.Add(new PitchEvent(0, PitchEventKind.KickOff, kickOffTaker.Player.PlayerId, null, 0.0));
    }

    public int Tick { get; private set; }

    public BallState Ball { get; private set; }

    public int HomeScore { get; private set; }

    public int AwayScore { get; private set; }

    public IReadOnlyList<PitchEvent> Events => _events;

    public IReadOnlyList<PlayerState> PlayerStates => _all.Select(r => r.ToState()).ToArray();

    public PlayerState GetPlayerState(int playerId) => FindRuntime(playerId).ToState();

    /// <summary>Utility-layer switch count for one player — the oscillation metric.</summary>
    public int DecisionSwitches(int playerId) => FindRuntime(playerId).Brain.DecisionSwitches;

    public void Run(int ticks)
    {
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "ticks must be >= 0.");
        for (int i = 0; i < ticks; i++)
            Step();
    }

    public void Step()
    {
        _influence.Rebuild(PlayerStates);

        // Decide from one immutable snapshot so player order cannot leak information.
        PlayerState[] snapshot = _all.Select(r => r.ToState()).ToArray();
        var intentions = new Intention[_all.Count];
        for (int i = 0; i < _all.Count; i++)
            intentions[i] = _all[i].Brain.Decide(Tick, BuildPerception(i, snapshot));

        int? ownerAtTickStart = Ball.OwnerPlayerId;
        ApplyBallRelease(intentions, ownerAtTickStart);
        ApplyTackles(intentions, ownerAtTickStart);
        ApplyMovement(intentions);
        UpdateBall();

        Tick++;
    }

    private void BuildSide(PitchTeam team, TeamSide side)
    {
        IReadOnlyList<FormationSlot> slots = team.Tactics.Formation.Slots;
        if (team.Players.Count != slots.Count)
            throw new ArgumentException($"{side} squad has {team.Players.Count} players but the formation '{team.Tactics.Formation.Name}' has {slots.Count} slots.");

        for (int i = 0; i < slots.Count; i++)
        {
            _all.Add(new Runtime(
                team.Players[i],
                new TacticalPlayerBrain(team.Players[i], slots[i], team.Tactics, _rng),
                side,
                slots[i].Role,
                _pitch.ToWorld(side, slots[i].BasePosition)));
        }
    }

    private PlayerPerception BuildPerception(int index, PlayerState[] snapshot)
    {
        PlayerState self = snapshot[index];
        var teammates = new List<PlayerState>(snapshot.Length / 2);
        var opponents = new List<PlayerState>(snapshot.Length / 2);
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (i == index)
                continue;
            (snapshot[i].Side == self.Side ? teammates : opponents).Add(snapshot[i]);
        }

        MatchPhase phase = Ball.OwnerPlayerId is int ownerId
            ? FindRuntime(ownerId).Side == self.Side ? MatchPhase.InPossession : MatchPhase.OutOfPossession
            : MatchPhase.Contested;

        return new PlayerPerception(self, Ball, teammates, opponents, phase, _influence, _pitch);
    }

    /// <summary>Execute the current owner's pass or shot, releasing the ball.</summary>
    private void ApplyBallRelease(Intention[] intentions, int? ownerId)
    {
        if (ownerId is null || Tick < _ownerControlReadyTick)
            return;

        int ownerIndex = _all.FindIndex(r => r.Player.PlayerId == ownerId);
        Runtime owner = _all[ownerIndex];

        switch (intentions[ownerIndex])
        {
            case Intention.Pass pass:
            {
                Vec2 direction = pass.TargetPosition - Ball.Position;
                double distance = direction.Length;
                double speed = Math.Clamp(
                    (BallFrictionPerSecond * distance) + PassArrivalSpeedMetresPerSecond,
                    8.0, MaxBallSpeedMetresPerSecond);
                double maxError = (0.10 * (1.0 - owner.Player.PassingSkill)) + 0.02;
                double angleError = ((_rng.NextDouble() * 2.0) - 1.0) * maxError;
                Ball = new BallState(Ball.Position, direction.Normalized().Rotated(angleError) * speed, null);
                _pendingPass = (owner.Player.PlayerId, pass.TargetPlayerId, Tick, Ball.Position);
                _events.Add(new PitchEvent(Tick, PitchEventKind.PassStarted, owner.Player.PlayerId, pass.TargetPlayerId, distance));
                break;
            }

            case Intention.Shoot shoot:
            {
                Vec2 direction = shoot.Target - Ball.Position;
                double speed = ShotSpeedMetresPerSecond + (owner.Player.Power * 6.0);
                double maxError = (0.09 * (1.0 - owner.Player.Finishing)) + 0.01;
                double angleError = ((_rng.NextDouble() * 2.0) - 1.0) * maxError;
                Ball = new BallState(Ball.Position, direction.Normalized().Rotated(angleError) * speed, null);
                _pendingPass = null;
                _shotInFlight = (owner.Player.PlayerId, owner.Player.Finishing);
                _events.Add(new PitchEvent(Tick, PitchEventKind.Shot, owner.Player.PlayerId, null, direction.Length));
                break;
            }
        }
    }

    /// <summary>Resolve tackle intentions in fixed player order; the first success takes the ball.</summary>
    private void ApplyTackles(Intention[] intentions, int? ownerAtTickStart)
    {
        if (ownerAtTickStart is null)
            return;

        for (int i = 0; i < _all.Count; i++)
        {
            if (intentions[i] is not Intention.Tackle tackle)
                continue;
            if (Ball.OwnerPlayerId != tackle.TargetPlayerId || tackle.TargetPlayerId != ownerAtTickStart)
                continue; // carrier already lost/released the ball this tick.

            Runtime tackler = _all[i];
            Runtime carrier = FindRuntime(tackle.TargetPlayerId);
            if (Tick < tackler.TackleCooldownUntil)
                continue;
            if (tackler.Position.DistanceTo(carrier.Position) > TackleResolveRadiusMetres)
                continue;

            tackler.TackleCooldownUntil = Tick + TackleCooldownTicks;
            _events.Add(new PitchEvent(Tick, PitchEventKind.TackleAttempt, tackler.Player.PlayerId, carrier.Player.PlayerId, tackler.Position.DistanceTo(carrier.Position)));

            double duel = tackler.Player.TacklingSkill / (tackler.Player.TacklingSkill + carrier.Player.DribblingSkill + 1e-9);
            double successChance = 0.25 + (0.55 * duel);
            if (_rng.NextDouble() < successChance)
            {
                SetOwner(tackler);
                _events.Add(new PitchEvent(Tick, PitchEventKind.TackleWon, tackler.Player.PlayerId, carrier.Player.PlayerId, 0.0));
            }
        }
    }

    private void ApplyMovement(Intention[] intentions)
    {
        for (int i = 0; i < _all.Count; i++)
        {
            Runtime runtime = _all[i];
            runtime.Velocity = intentions[i] switch
            {
                Intention.Move move => move.DesiredVelocity.ClampLength(runtime.Player.MaxSpeed),
                Intention.Idle => Vec2.Zero,
                Intention.Tackle => runtime.Velocity * 0.5, // mid-lunge: momentum bleeds off.
                _ => runtime.Velocity * 0.85, // just released the ball: decelerate.
            };
            runtime.Position = _pitch.Clamp(runtime.Position + (runtime.Velocity * TickSeconds));
        }
    }

    private void UpdateBall()
    {
        if (Ball.OwnerPlayerId is int ownerId)
        {
            // Owned ball travels glued slightly ahead of its carrier.
            Runtime owner = FindRuntime(ownerId);
            Vec2 lead = owner.Velocity.Length > 0.1 ? owner.Velocity.Normalized() * 0.5 : Vec2.Zero;
            Ball = new BallState(_pitch.Clamp(owner.Position + lead), owner.Velocity, ownerId);
            return;
        }

        double friction = _shotInFlight is null ? BallFrictionPerSecond : ShotFrictionPerSecond;
        Vec2 position = Ball.Position + (Ball.Velocity * TickSeconds);
        Vec2 velocity = Ball.Velocity * Math.Max(0.0, 1.0 - (friction * TickSeconds));

        if (position.X <= 0.0 || position.X >= _pitch.Length)
        {
            ResolveBallOverEndLine(position);
            return;
        }

        if (position.Y <= 0.0 || position.Y >= _pitch.Width)
        {
            GiveBallTo(NearestRuntime(_pitch.Clamp(position), _ => true), PitchEventKind.BallRecovered);
            return;
        }

        Ball = new BallState(position, velocity, null);
        TryPickup();
    }

    private void ResolveBallOverEndLine(Vec2 position)
    {
        bool overHomeLine = position.X <= 0.0;
        bool insideGoalMouth = Math.Abs(position.Y - (_pitch.Width / 2.0)) <= _pitch.GoalWidth / 2.0;
        if (insideGoalMouth)
        {
            TeamSide concedingSide = overHomeLine ? TeamSide.Home : TeamSide.Away;

            // Only a struck shot can be a goal. A loose non-shot that threads the mouth (a stray
            // deflection/back-pass) is collected by the keeper instead of being an unsaveable
            // goal with an invalid scorer id. TODO: model own goals explicitly once wanted.
            if (_shotInFlight is not { } shot)
            {
                GiveBallTo(_all.First(r => r.Side == concedingSide && r.Role == PlayerRole.Goalkeeper), PitchEventKind.BallRecovered);
                return;
            }

            if (TrySave(concedingSide, _pitch.Clamp(position)))
                return;

            _events.Add(new PitchEvent(Tick, PitchEventKind.Goal, shot.ShooterId, null, 0.0));
            if (concedingSide == TeamSide.Away)
                HomeScore++;
            else
                AwayScore++;
            ResetForKickOff(concededBy: concedingSide);
            return;
        }

        // MVP restart: out over the end line simply goes to the nearest player.
        // TODO: proper corners/goal kicks/throw-ins are set-piece work for a later module.
        GiveBallTo(NearestRuntime(_pitch.Clamp(position), _ => true), PitchEventKind.BallRecovered);
    }

    /// <summary>
    /// MVP shot-stopping: when a struck ball crosses inside the mouth, a close-enough keeper
    /// saves with a chance shaped by the shooter's finishing. TODO: positioning/diving keeper
    /// AI is future work — the keeper here only sweeps, presses (rarely), and stops shots.
    /// </summary>
    private bool TrySave(TeamSide concedingSide, Vec2 crossingPoint)
    {
        if (_shotInFlight is not { } shot)
            return false;

        Runtime keeper = _all.First(r => r.Side == concedingSide && r.Role == PlayerRole.Goalkeeper);
        double goalLineX = _pitch.DefendedGoal(concedingSide).X;
        bool inReach = Math.Abs(keeper.Position.Y - crossingPoint.Y) <= GoalkeeperReachMetres
            && Math.Abs(keeper.Position.X - goalLineX) <= 8.0;
        if (!inReach)
            return false;

        double saveChance = 0.92 - (0.35 * shot.Finishing);
        if (_rng.NextDouble() >= saveChance)
            return false;

        GiveBallTo(keeper, PitchEventKind.Save);
        return true;
    }

    private void ResetForKickOff(TeamSide concededBy)
    {
        foreach (Runtime runtime in _all)
        {
            runtime.Position = runtime.KickOffPosition;
            runtime.Velocity = Vec2.Zero;
        }

        Runtime taker = NearestRuntime(_pitch.Centre, r => r.Side == concededBy);
        SetOwner(taker);
        _events.Add(new PitchEvent(Tick, PitchEventKind.KickOff, taker.Player.PlayerId, null, 0.0));
    }

    private void TryPickup()
    {
        Runtime? winner = null;
        double winnerDistance = double.MaxValue;
        double ballSpeed = Ball.Velocity.Length;
        foreach (Runtime runtime in _all)
        {
            if (_pendingPass is { } pass && pass.FromPlayerId == runtime.Player.PlayerId && Tick - pass.ReleaseTick < PasserPickupLockTicks)
                continue; // the passer cannot instantly re-collect his own pass.

            bool isIntendedReceiver = _pendingPass is { } p && p.ToPlayerId == runtime.Player.PlayerId;
            if (!isIntendedReceiver && ballSpeed > ControllableSpeedMetresPerSecond)
                continue; // too hot to steal in stride.

            double distance = runtime.Position.DistanceTo(Ball.Position);
            if (distance <= ControlRadiusMetres
                && (distance < winnerDistance || (distance == winnerDistance && runtime.Player.PlayerId < winner!.Player.PlayerId)))
            {
                winner = runtime;
                winnerDistance = distance;
            }
        }

        if (winner is null)
            return;

        PitchEventKind kind = PitchEventKind.BallRecovered;
        double value = 0.0;
        if (_pendingPass is { } pending)
        {
            Runtime passer = FindRuntime(pending.FromPlayerId);
            if (winner.Side != passer.Side)
                kind = PitchEventKind.Interception;
            else if (winner.Player.PlayerId == pending.ToPlayerId)
            {
                kind = PitchEventKind.PassCompleted;
                value = pending.Origin.DistanceTo(Ball.Position);
            }
        }

        GiveBallTo(winner, kind, value);
    }

    private void GiveBallTo(Runtime runtime, PitchEventKind kind, double value = 0.0)
    {
        int? otherId = _pendingPass?.FromPlayerId;
        SetOwner(runtime);
        _events.Add(new PitchEvent(Tick, kind, runtime.Player.PlayerId, otherId, value));
    }

    /// <summary>Hand possession over, starting the new owner's first-touch control window.</summary>
    private void SetOwner(Runtime runtime)
    {
        Ball = new BallState(runtime.Position, Vec2.Zero, runtime.Player.PlayerId);
        _pendingPass = null;
        _shotInFlight = null;
        _ownerControlReadyTick = Tick + FirstTouchTicks;
    }

    private Runtime FindRuntime(int playerId)
        => _all.FirstOrDefault(r => r.Player.PlayerId == playerId)
            ?? throw new InvalidOperationException($"No player with id {playerId} is on the pitch.");

    private Runtime NearestRuntime(Vec2 point, Func<Runtime, bool> filter)
    {
        Runtime? best = null;
        double bestDistance = double.MaxValue;
        foreach (Runtime runtime in _all)
        {
            if (!filter(runtime))
                continue;
            double distance = runtime.Position.DistanceTo(point);
            if (distance < bestDistance || (distance == bestDistance && runtime.Player.PlayerId < best!.Player.PlayerId))
            {
                best = runtime;
                bestDistance = distance;
            }
        }

        return best ?? throw new InvalidOperationException("No player matched the nearest-player filter.");
    }

    private sealed class Runtime
    {
        public Runtime(PitchPlayer player, TacticalPlayerBrain brain, TeamSide side, PlayerRole role, Vec2 kickOffPosition)
        {
            Player = player;
            Brain = brain;
            Side = side;
            Role = role;
            KickOffPosition = kickOffPosition;
            Position = kickOffPosition;
        }

        public PitchPlayer Player { get; }

        public TacticalPlayerBrain Brain { get; }

        public TeamSide Side { get; }

        public PlayerRole Role { get; }

        public Vec2 KickOffPosition { get; }

        public Vec2 Position { get; set; }

        public Vec2 Velocity { get; set; } = Vec2.Zero;

        public int TackleCooldownUntil { get; set; }

        public PlayerState ToState() => new(Player.PlayerId, Side, Position, Velocity);
    }
}
