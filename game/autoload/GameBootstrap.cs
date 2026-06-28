using Godot;
using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.MatchEngine;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using SoccerSim.Core.Time;
using SoccerSim.Infrastructure.Sqlite;

namespace SoccerDreamGame.Autoload;

/// <summary>
/// Composition root (autoload, listed first). Opens the SQLite save database, applies
/// migrations, and wires the engine-agnostic core services together. Every other
/// autoload and scene reads the live services from <see cref="Instance"/>.
/// </summary>
public partial class GameBootstrap : Node
{
    public static GameBootstrap Instance { get; private set; } = null!;

    public ITimeManager Time { get; private set; } = null!;

    public IEventManager Events { get; private set; } = null!;

    public ISimulationLODManager Lod { get; private set; } = null!;

    /// <summary>On-demand simulation of the player's rendered fixture (Tier 1 match scene).</summary>
    public IMatchPresenter Match { get; private set; } = null!;

    /// <summary>
    /// The active career / save-state: who the human controls. Sourced from the database
    /// at startup. The player's club is the one reserved for the rendered match scene
    /// instead of background LOD resolution; null only if no career has been created yet.
    /// </summary>
    public CareerState? Career { get; private set; }

    /// <summary>
    /// Root world seed. Every simulation stream derives from this, so a given save replays
    /// identically. TODO: persist this per-career in the save-state instead of a constant.
    /// </summary>
    public ulong MasterSeed { get; } = 0xD1CED00D2026UL;

    // One deterministic generator, shared by the background resolvers and the match engine and
    // derived from the master seed (no System.Random anywhere). Per-fixture isolation is available
    // via DeterministicRng.CreateStream(MasterSeed, fixtureId, ...) once the LOD threads a seed per match.
    private IDeterministicRandom _rng = null!;
    private IFixtureGateway _gateway = null!;
    private SqliteConnection? _connection;

    public override void _Ready()
    {
        Instance = this;

        // user:// resolves to a writable per-user directory on every desktop platform.
        string databasePath = ProjectSettings.GlobalizePath("user://save.db");
        var factory = SqliteConnectionFactory.ForFile(databasePath);
        // Dev: apply the seed migration too so a fresh save has a world (teams, players,
        // and a Tier 1 fixture) to simulate. Recorded once, so it is a no-op thereafter.
        new MigrationRunner(factory).Migrate(includeSeeds: true);
        _connection = factory.Open();

        _rng = DeterministicRng.Create(MasterSeed);
        _gateway = new SqliteFixtureGateway(_connection);

        // Who the human controls — sourced from the persisted career save-state rather than
        // hardcoded. The reserved-for-rendering club is this player's team.
        Career = new SqliteCareerService(_connection).GetActiveCareer();
        int? humanTeamId = Career?.HumanTeamId;

        Events = new EventManager(BuildEventDefinitions());
        Lod = new SimulationLODManager(_gateway, new ILeagueResolver[]
        {
            new Tier1MatchResolver(_rng),                                  // full deterministic tick engine
            new StatisticalMatchResolver(SimulationTier.MajorForeign, _rng), // Tier 2: double-Poisson
            new StatisticalMatchResolver(SimulationTier.Minor, _rng),        // Tier 3: double-Poisson (no form)
        }, humanTeamId);
        Match = new MatchPresentationService(_gateway, _rng, humanTeamId);
        Time = new TimeManager(new GameClock(new DateTime(2026, 8, 1)), Events, Lod, BuildRollContext);

        string human = Career is null ? "(none)" : $"player {Career.HumanPlayerId}, team {Career.HumanTeamId}";
        GD.Print($"[GameBootstrap] Core initialised. Save database: {databasePath}. Human: {human}");
    }

    public override void _ExitTree() => _connection?.Dispose();

    /// <summary>True if a club with this id exists in the world. Used by the match-entry guard.</summary>
    public bool ClubExists(int clubId)
    {
        using SqliteCommand command = _connection!.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Teams WHERE Id = $id);";
        command.Parameters.AddWithValue("$id", clubId);
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

    /// <summary>
    /// The human club's next unplayed fixture (the one reserved for the rendered match scene),
    /// or null if none remain. Lets the menu hand a concrete fixture to <c>GameModeManager.EnterMatch</c>.
    /// </summary>
    public SoccerSim.Core.Domain.Match? PeekNextHumanFixture()
    {
        int? matchId = _gateway.GetNextUnplayedMatchId(SimulationTier.ActiveHuman, Career?.HumanTeamId);
        return matchId is int id ? _gateway.GetMatchContext(id)?.Match : null;
    }

    private EventRollContext BuildRollContext(DateTime date) =>
        new(Career?.HumanPlayerId ?? 1, Career?.TraitWeights ?? new Dictionary<string, int>(), 1.0, _rng);

    private static IReadOnlyList<EventDefinition> BuildEventDefinitions() => new[]
    {
        new EventDefinition("contract_offer", EventTier.High, 0.01, new Dictionary<string, double>()),
        new EventDefinition("press_conference", EventTier.Medium, 0.03,
            new Dictionary<string, double> { ["aggression"] = 0.05 }),
        new EventDefinition("flight_delay", EventTier.Low, 0.02, new Dictionary<string, double>())
        {
            LowStakesStatDeltas = new[] { new StatDelta("morale", -1) },
        },
    };
}
