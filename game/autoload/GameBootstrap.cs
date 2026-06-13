using Godot;
using Microsoft.Data.Sqlite;
using SoccerSim.Core.Events;
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

    private readonly SeededRandom _rng = new();
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

        var gateway = new SqliteFixtureGateway(_connection);

        Events = new EventManager(BuildEventDefinitions());
        Lod = new SimulationLODManager(gateway, new ILeagueResolver[]
        {
            new Tier1MatchResolver(_rng),
            new Tier2EloResolver(_rng),
            new Tier3MathResolver(_rng),
        });
        Match = new MatchPresentationService(gateway, new MatchEngine(_rng));
        Time = new TimeManager(new GameClock(new DateTime(2026, 8, 1)), Events, Lod, BuildRollContext);

        GD.Print("[GameBootstrap] Core initialised. Save database: ", databasePath);
    }

    public override void _ExitTree() => _connection?.Dispose();

    private EventRollContext BuildRollContext(DateTime date) =>
        // TODO: source the active player's static trait weights from the database.
        new(1, new Dictionary<string, int>(), 1.0, _rng);

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
