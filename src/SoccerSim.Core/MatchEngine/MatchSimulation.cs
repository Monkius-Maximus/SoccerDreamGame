using SoccerSim.Core.Domain;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;

namespace SoccerSim.Core.MatchEngine;

/// <summary>Tunables for a match run. Fixed timestep is mandatory for reproducibility.</summary>
public sealed record MatchSettings
{
    /// <summary>Logical seconds advanced per <see cref="MatchSimulation.Step"/> (must be constant).</summary>
    public double FixedDt { get; init; } = 1.0 / 60.0;

    /// <summary>Regulation length in logical seconds (90 minutes).</summary>
    public int RegulationSeconds { get; init; } = 90 * 60;

    /// <summary>When the half-time marker is emitted (45 minutes).</summary>
    public int HalfTimeSeconds { get; init; } = 45 * 60;

    public static MatchSettings Default { get; } = new();
}

/// <summary>
/// The deterministic, render-agnostic match engine (the headless Tier 1 path). It owns the fixed
/// timestep loop: every <see cref="Step"/> runs a fixed, deterministic order — player decisions →
/// physics (kicks + integration) → geometric events (goal / out of play via the shared last touch)
/// → referee judgement → emission. <see cref="RunToCompletion"/> drives Step to full time and
/// returns the same <see cref="MatchResult"/> every tier emits. A future 3D scene will instead call
/// <see cref="Step"/> from <c>_PhysicsProcess</c> and read the post-tick state — Step is the single
/// advance gate shared by both paths.
///
/// <para>
/// Coordinate frame (metres): X is pitch length [0, <see cref="PitchLength"/>], Z is width
/// [0, <see cref="PitchWidth"/>], Y is up. Home attacks +X (its goal is at X = 0); away attacks -X.
/// The first squad member is treated as the goalkeeper (the data has no position/keeper flag yet).
/// </para>
/// </summary>
public sealed class MatchSimulation
{
    // --- Pitch / goal geometry (FIFA-ish; calibratable) ---
    public const double PitchLength = 105.0;
    public const double PitchWidth = 68.0;
    private const double GoalHalfWidth = 3.66;   // goal mouth half-width (7.32 m goal)
    private const double CrossbarHeight = 2.44;
    private const double GoalAimHeight = 0.3;     // brains aim along the ground

    // --- Shot / kick tuning (m/s, radians) ---
    private const double ControlRadius = PlaceholderPlayerBrain.ControlRadius;
    private const double ShotMinSpeed = 18.0;
    private const double ShotMaxSpeed = 30.0;
    private const double PassSpeed = 16.0;
    private const double ClearSpeed = 24.0;
    private const double LoftRatio = 0.12;        // upward share added to a struck ball
    private const double MaxAimJitterRad = 0.18;  // ~10 degrees of seeded aim spread
    private const double OnTargetJitterRad = 0.08;
    private const double MinSpin = 10.0;
    private const double MaxSpin = 60.0;
    private const double PlayerDecel = 0.8;       // velocity kept per tick when not actively moving
    private const double GoalKickInset = 6.0;
    private const int RequiredSquadSize = 11;

    private readonly int _matchId;
    private readonly int _homeTeamId;
    private readonly int _awayTeamId;
    private readonly MatchConditions _conditions;
    private readonly IDeterministicRandom _rng;
    private readonly IPlayerBrain _brain;
    private readonly IReferee _referee;
    private readonly MatchSettings _settings;

    private readonly PlayerState[] _homePlayers;
    private readonly PlayerState[] _awayPlayers;
    private readonly Intention[] _homeIntentions;
    private readonly Intention[] _awayIntentions;

    private readonly Vector3 _homeAttackGoal;
    private readonly Vector3 _homeOwnGoal;
    private readonly Vector3 _awayAttackGoal;
    private readonly Vector3 _awayOwnGoal;

    private readonly BallState _ball;
    private readonly List<MatchEvent> _events = new();
    private readonly List<ScorerLine> _scorers = new();

    private int _tick;
    private double _clockSeconds;
    private int _homeGoals;
    private int _awayGoals;
    private int? _lastTouchTeamId;
    private int? _lastTouchPlayerId;
    private bool _halfTimeEmitted;

    private int _homeShots, _awayShots, _homeOnTarget, _awayOnTarget;
    private long _homePossessionTicks, _awayPossessionTicks;

    public MatchSimulation(
        int matchId,
        TeamSnapshot home,
        TeamSnapshot away,
        MatchConditions conditions,
        IDeterministicRandom rng,
        IPlayerBrain brain,
        IReferee referee,
        MatchSettings? settings = null)
    {
        if (home is null) throw new ArgumentNullException(nameof(home));
        if (away is null) throw new ArgumentNullException(nameof(away));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _brain = brain ?? throw new ArgumentNullException(nameof(brain));
        _referee = referee ?? throw new ArgumentNullException(nameof(referee));
        _settings = settings ?? MatchSettings.Default;

        _matchId = matchId;
        _homeTeamId = home.TeamId;
        _awayTeamId = away.TeamId;
        _conditions = conditions;

        _homePlayers = BuildSquad(home, attackingPositiveX: true);
        _awayPlayers = BuildSquad(away, attackingPositiveX: false);
        _homeIntentions = new Intention[_homePlayers.Length];
        _awayIntentions = new Intention[_awayPlayers.Length];

        _homeAttackGoal = new Vector3(PitchLength, GoalAimHeight, PitchWidth / 2);
        _homeOwnGoal = new Vector3(0, GoalAimHeight, PitchWidth / 2);
        _awayAttackGoal = _homeOwnGoal;
        _awayOwnGoal = _homeAttackGoal;

        _ball = new BallState(CentreSpot(), Vector3.Zero, Vector3.Zero);
        _lastTouchTeamId = _homeTeamId; // home kicks off
    }

    public int Tick => _tick;

    public int CurrentMinute => (int)(_clockSeconds / 60.0);

    public int HomeGoals => _homeGoals;

    public int AwayGoals => _awayGoals;

    public BallState Ball => _ball;

    public IReadOnlyList<MatchEvent> EventStream => _events;

    public IReadOnlyList<PlayerState> HomePlayers => _homePlayers;

    public IReadOnlyList<PlayerState> AwayPlayers => _awayPlayers;

    /// <summary>Advance the match by exactly one fixed tick in the canonical, deterministic order.</summary>
    public void Step(double fixedDt)
    {
        if (fixedDt <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(fixedDt), $"fixedDt must be > 0, was {fixedDt}.");

        // 1) Decisions for every player (no side effects on shared state).
        for (int i = 0; i < _homePlayers.Length; i++)
            _homeIntentions[i] = _brain.Decide(_tick, Perceive(_homePlayers[i], _homePlayers, _awayPlayers, _homeAttackGoal, _homeOwnGoal));
        for (int i = 0; i < _awayPlayers.Length; i++)
            _awayIntentions[i] = _brain.Decide(_tick, Perceive(_awayPlayers[i], _awayPlayers, _homePlayers, _awayAttackGoal, _awayOwnGoal));

        // 2) Physics: the first player in control kicks, then everyone integrates, then the ball.
        ApplyKicks();
        IntegratePlayers(_homePlayers, _homeIntentions, fixedDt);
        IntegratePlayers(_awayPlayers, _awayIntentions, fixedDt);
        BallPhysics.Step(_ball, _conditions, fixedDt);

        if (_lastTouchTeamId == _homeTeamId) _homePossessionTicks++;
        else if (_lastTouchTeamId == _awayTeamId) _awayPossessionTicks++;

        // 3) Geometric events (goal / out of play), resolved from positions + last touch.
        DetectGeometricEvents();

        // 4) Referee judgement (pure read; NoOp for now).
        foreach (MatchEvent judged in _referee.Evaluate(BuildStateView()))
            _events.Add(judged);

        // 5) Advance the clock; emit the half-time marker once.
        _tick++;
        _clockSeconds += fixedDt;
        if (!_halfTimeEmitted && _clockSeconds >= _settings.HalfTimeSeconds)
        {
            _events.Add(new MatchEvent.HalfTime(_tick, CurrentMinute));
            _halfTimeEmitted = true;
        }
    }

    /// <summary>Run the whole match headlessly and return the persisted result contract.</summary>
    public MatchResult RunToCompletion()
    {
        _events.Add(new MatchEvent.KickOff(0, 0));

        while (_clockSeconds < _settings.RegulationSeconds)
            Step(_settings.FixedDt);

        _events.Add(new MatchEvent.FullTime(_tick, CurrentMinute));
        return BuildResult();
    }

    /// <summary>Box score after a completed run (possession sums to 100).</summary>
    public MatchBoxScore BoxScore()
    {
        long total = _homePossessionTicks + _awayPossessionTicks;
        int homePossession = total == 0 ? 50 : (int)Math.Round(_homePossessionTicks * 100.0 / total);
        return new MatchBoxScore(homePossession, 100 - homePossession, _homeShots, _awayShots, _homeOnTarget, _awayOnTarget);
    }

    private PlayerPerception Perceive(PlayerState self, PlayerState[] team, PlayerState[] opponents, Vector3 attackGoal, Vector3 ownGoal)
        => new(self, _ball.Position, _ball.Velocity, team, opponents, attackGoal, ownGoal);

    private MatchStateView BuildStateView()
        => new(_tick, CurrentMinute, _ball, _homePlayers, _awayPlayers, _lastTouchPlayerId, _lastTouchTeamId, _homeGoals, _awayGoals);

    // --- Phase 2 helpers -----------------------------------------------------

    private void ApplyKicks()
    {
        // Deterministic order: home (by squad index) then away; the first controller acts.
        if (TryKick(_homePlayers, _homeIntentions, _homeAttackGoal)) return;
        TryKick(_awayPlayers, _awayIntentions, _awayAttackGoal);
    }

    private bool TryKick(PlayerState[] players, Intention[] intentions, Vector3 attackGoal)
    {
        for (int i = 0; i < players.Length; i++)
        {
            PlayerState p = players[i];
            if (p.Position.PlanarDistanceTo(_ball.Position) > ControlRadius)
                continue;

            if (p.IsGoalkeeper)
            {
                ClearUpfield(p, attackGoal);
                return true;
            }

            switch (intentions[i])
            {
                case Intention.Shoot shoot:
                    ShootBall(p, shoot.Direction);
                    return true;
                case Intention.Pass pass:
                    PassBall(p, pass.TargetPlayerId, players);
                    return true;
            }
        }

        return false;
    }

    private void ShootBall(PlayerState shooter, Vector3 direction)
    {
        double power = ShotMinSpeed + ((ShotMaxSpeed - ShotMinSpeed) * (shooter.Attributes.Shooting / 20.0));
        double aimJitter = (_rng.NextDouble() - 0.5) * (2.0 * MaxAimJitterRad);
        Vector3 planar = RotateAroundY(new Vector3(direction.X, 0, direction.Z).Normalized(), aimJitter);
        Vector3 launch = (planar + new Vector3(0, LoftRatio, 0)).Normalized();

        _ball.Velocity = launch * power;
        double spinSign = _rng.NextDouble() < 0.5 ? -1.0 : 1.0;
        double spinMagnitude = MinSpin + ((MaxSpin - MinSpin) * _rng.NextDouble());
        _ball.Spin = new Vector3(0, spinSign * spinMagnitude, 0);

        SetLastTouch(shooter);
        RecordShot(shooter.TeamId, onTarget: Math.Abs(aimJitter) <= OnTargetJitterRad);
    }

    private void PassBall(PlayerState passer, int targetId, PlayerState[] team)
    {
        PlayerState? target = null;
        foreach (PlayerState mate in team)
        {
            if (mate.PlayerId == targetId)
            {
                target = mate;
                break;
            }
        }

        Vector3 toTarget = target is null
            ? (_homeTeamId == passer.TeamId ? _homeAttackGoal : _awayAttackGoal) - _ball.Position
            : target.Position - _ball.Position;

        _ball.Velocity = new Vector3(toTarget.X, 0, toTarget.Z).Normalized() * PassSpeed;
        _ball.Spin = Vector3.Zero;
        SetLastTouch(passer);
    }

    private void ClearUpfield(PlayerState keeper, Vector3 attackGoal)
    {
        Vector3 dir = new Vector3(attackGoal.X - _ball.Position.X, 0, 0).Normalized();
        _ball.Velocity = (dir + new Vector3(0, LoftRatio, 0)).Normalized() * ClearSpeed;
        _ball.Spin = Vector3.Zero;
        SetLastTouch(keeper);
    }

    private static void IntegratePlayers(PlayerState[] players, Intention[] intentions, double dt)
    {
        for (int i = 0; i < players.Length; i++)
        {
            PlayerState p = players[i];
            p.Velocity = intentions[i] is Intention.Move move
                ? new Vector3(move.Direction.X, 0, move.Direction.Z).Normalized() * p.MaxSpeed
                : p.Velocity * PlayerDecel;

            Vector3 next = p.Position + (p.Velocity * dt);
            p.Position = new Vector3(
                Math.Clamp(next.X, 0, PitchLength),
                0,
                Math.Clamp(next.Z, 0, PitchWidth));
        }
    }

    // --- Phase 3 helpers -----------------------------------------------------

    private void DetectGeometricEvents()
    {
        Vector3 pos = _ball.Position;

        if (pos.X >= PitchLength) // crossed away's goal line (home attacks +X)
        {
            if (InGoalMouth(pos)) ScoreGoal(_homeTeamId);
            else ResolveByline(defendingTeamId: _awayTeamId, attackingTeamId: _homeTeamId, goalLineX: PitchLength);
        }
        else if (pos.X <= 0) // crossed home's goal line
        {
            if (InGoalMouth(pos)) ScoreGoal(_awayTeamId);
            else ResolveByline(defendingTeamId: _homeTeamId, attackingTeamId: _awayTeamId, goalLineX: 0);
        }
        else if (pos.Z < 0 || pos.Z > PitchWidth)
        {
            ResolveThrowIn(pos);
        }
    }

    private static bool InGoalMouth(Vector3 pos)
        => pos.Y <= CrossbarHeight
           && pos.Z >= (PitchWidth / 2) - GoalHalfWidth
           && pos.Z <= (PitchWidth / 2) + GoalHalfWidth;

    private void ScoreGoal(int scoringTeamId)
    {
        int? scorer = _lastTouchTeamId == scoringTeamId ? _lastTouchPlayerId : null;
        if (scoringTeamId == _homeTeamId) _homeGoals++;
        else _awayGoals++;

        _events.Add(new MatchEvent.GoalScored(_tick, CurrentMinute, scoringTeamId, scorer));
        if (scorer is int scorerId)
            _scorers.Add(new ScorerLine(scorerId, Math.Max(1, CurrentMinute)));

        // Restart from the centre; the conceding side kicks off.
        ResetBall(CentreSpot(), scoringTeamId == _homeTeamId ? _awayTeamId : _homeTeamId);
    }

    private void ResolveByline(int defendingTeamId, int attackingTeamId, double goalLineX)
    {
        bool defenderTouchedLast = _lastTouchTeamId == defendingTeamId;
        RestartType restart = defenderTouchedLast ? RestartType.Corner : RestartType.GoalKick;
        int restartTeam = defenderTouchedLast ? attackingTeamId : defendingTeamId;

        Vector3 spot = restart == RestartType.Corner
            ? new Vector3(goalLineX, 0, _ball.Position.Z < PitchWidth / 2 ? 0 : PitchWidth)
            : new Vector3(goalLineX == 0 ? GoalKickInset : PitchLength - GoalKickInset, 0, PitchWidth / 2);

        _events.Add(new MatchEvent.BallOutOfPlay(_tick, CurrentMinute, restart, restartTeam));
        ResetBall(spot, restartTeam);
    }

    private void ResolveThrowIn(Vector3 pos)
    {
        int restartTeam = _lastTouchTeamId == _homeTeamId ? _awayTeamId : _homeTeamId;
        Vector3 spot = new(Math.Clamp(pos.X, 0, PitchLength), 0, pos.Z < 0 ? 0 : PitchWidth);

        _events.Add(new MatchEvent.BallOutOfPlay(_tick, CurrentMinute, RestartType.ThrowIn, restartTeam));
        ResetBall(spot, restartTeam);
    }

    private void ResetBall(Vector3 position, int possessionTeamId)
    {
        _ball.Position = position.WithY(BallPhysics.Radius);
        _ball.Velocity = Vector3.Zero;
        _ball.Spin = Vector3.Zero;
        _lastTouchTeamId = possessionTeamId;
        _lastTouchPlayerId = null;
    }

    // --- Result + setup helpers ---------------------------------------------

    private MatchResult BuildResult()
    {
        var goalsByPlayer = new Dictionary<int, int>();
        foreach (ScorerLine scorer in _scorers)
            goalsByPlayer[scorer.PlayerId] = goalsByPlayer.GetValueOrDefault(scorer.PlayerId) + 1;

        var ratings = new Dictionary<int, double>();
        RateSquad(ratings, _homePlayers, ResultBonus(_homeGoals, _awayGoals), goalsByPlayer);
        RateSquad(ratings, _awayPlayers, ResultBonus(_awayGoals, _homeGoals), goalsByPlayer);

        return new MatchResult(_matchId, _homeGoals, _awayGoals, _scorers, ratings);
    }

    private static void RateSquad(Dictionary<int, double> ratings, PlayerState[] players, double resultBonus, IReadOnlyDictionary<int, int> goalsByPlayer)
    {
        foreach (PlayerState p in players)
        {
            double rating = 6.0 + resultBonus + (goalsByPlayer.GetValueOrDefault(p.PlayerId) * 0.8);
            ratings[p.PlayerId] = Math.Round(Math.Clamp(rating, 1.0, 10.0), 1);
        }
    }

    private static double ResultBonus(int goalsFor, int goalsAgainst)
        => goalsFor > goalsAgainst ? 0.6 : goalsFor == goalsAgainst ? 0.0 : -0.4;

    private void SetLastTouch(PlayerState p)
    {
        _lastTouchTeamId = p.TeamId;
        _lastTouchPlayerId = p.PlayerId;
    }

    private void RecordShot(int teamId, bool onTarget)
    {
        if (teamId == _homeTeamId)
        {
            _homeShots++;
            if (onTarget) _homeOnTarget++;
        }
        else
        {
            _awayShots++;
            if (onTarget) _awayOnTarget++;
        }
    }

    private static Vector3 CentreSpot() => new(PitchLength / 2, BallPhysics.Radius, PitchWidth / 2);

    private static Vector3 RotateAroundY(Vector3 v, double angle)
    {
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        return new Vector3((v.X * c) - (v.Z * s), v.Y, (v.X * s) + (v.Z * c));
    }

    private PlayerState[] BuildSquad(TeamSnapshot team, bool attackingPositiveX)
    {
        if (team.Players.Count < RequiredSquadSize)
            throw new InvalidOperationException(
                $"Team {team.TeamId} has an incomplete squad: {team.Players.Count} players with attributes, needs {RequiredSquadSize}.");

        var squad = new PlayerState[RequiredSquadSize];
        for (int i = 0; i < RequiredSquadSize; i++)
        {
            PlayerSnapshot snap = team.Players[i];
            Vector3 home = FormationSlot(i, attackingPositiveX);
            squad[i] = new PlayerState
            {
                PlayerId = snap.PlayerId,
                TeamId = team.TeamId,
                Attributes = snap.Attributes,
                IsGoalkeeper = i == 0, // no keeper flag in the data yet; first squad member keeps goal.
                HomePosition = home,
                Position = home,
                Velocity = Vector3.Zero,
            };
        }

        return squad;
    }

    /// <summary>A simple 4-3-3 slot for index 0..10, mirrored for the side attacking -X.</summary>
    private static Vector3 FormationSlot(int index, bool attackingPositiveX)
    {
        double w = PitchWidth;
        (double X, double Z) slot = index switch
        {
            0 => (3.0, w / 2),                                   // GK
            1 => (18.0, w * 0.2), 2 => (18.0, w * 0.4), 3 => (18.0, w * 0.6), 4 => (18.0, w * 0.8), // defence
            5 => (40.0, w * 0.25), 6 => (40.0, w * 0.5), 7 => (40.0, w * 0.75),                     // midfield
            8 => (60.0, w * 0.3), 9 => (60.0, w * 0.5), 10 => (60.0, w * 0.7),                      // attack
            _ => (40.0, w / 2),
        };

        double x = attackingPositiveX ? slot.X : PitchLength - slot.X;
        return new Vector3(x, 0, slot.Z);
    }
}
