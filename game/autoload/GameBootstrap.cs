using Godot;
using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
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
    /// The active career / save-state: who the human controls, and the world seed. Sourced from
    /// the database at startup. The player's club is the one reserved for the rendered match scene
    /// instead of background LOD resolution.
    /// </summary>
    public CareerState Career { get; private set; } = null!;

    /// <summary>
    /// Root world seed, read from the active career. Every simulation stream derives from it, so a
    /// given save replays identically — and a different save replays as its own world, which a
    /// constant here could never do.
    /// </summary>
    public ulong MasterSeed => Career.MasterSeed;

    // One isolated stream per concern, all derived from the master seed. Keeping them apart is the
    // point: draining the match stream must not shift what the life-event stream produces next.
    // Per-fixture isolation is available via RandomStream.Create(MasterSeed, StreamName.MatchSimulation,
    // fixtureId, ...) once the LOD threads a seed per match.
    private RandomStream _matchRng = null!;
    private RandomStream _backgroundRng = null!;
    private RandomStream _lifeRng = null!;
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

        // Who the human controls AND the world seed — both sourced from the persisted career
        // save-state rather than hardcoded. This has to happen before the streams exist, because
        // the streams derive from the seed it carries.
        Career = new SqliteCareerService(_connection).GetActiveCareer()
            ?? throw new InvalidOperationException(
                $"No active career in '{databasePath}'. There is no world seed to simulate from, " +
                "and inventing one would generate a world this save was never written against.");

        int humanTeamId = Career.HumanTeamId;

        _matchRng = RandomStream.Create(MasterSeed, StreamName.MatchSimulation);
        _backgroundRng = RandomStream.Create(MasterSeed, StreamName.BackgroundSimulation);
        _lifeRng = RandomStream.Create(MasterSeed, StreamName.LifeEvents);
        _gateway = new SqliteFixtureGateway(_connection);

        Events = new EventManager(BuildEventDefinitions());
        Lod = new SimulationLODManager(_gateway, new ILeagueResolver[]
        {
            new Tier1MatchResolver(_matchRng),
            new Tier2EloResolver(_backgroundRng),
            new Tier3MathResolver(_backgroundRng),
        }, humanTeamId);
        Match = new MatchPresentationService(_gateway, new MatchEngine(_matchRng), humanTeamId);
        Time = new TimeManager(new GameClock(new DateTime(2026, 8, 1)), Events, Lod, BuildRollContext, _lifeRng);

        GD.Print($"[GameBootstrap] Core initialised. Save database: {databasePath}. " +
            $"Human: player {Career.HumanPlayerId}, team {Career.HumanTeamId}. Master seed: 0x{MasterSeed:X}.");
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
        int? matchId = _gateway.GetNextUnplayedMatchId(SimulationTier.ActiveHuman, Career.HumanTeamId);
        return matchId is int id ? _gateway.GetMatchContext(id)?.Match : null;
    }

    private EventRollContext BuildRollContext(DateTime date) =>
        new(Career.HumanPlayerId, Career.TraitWeights, 1.0, _lifeRng);

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
