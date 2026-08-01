using Godot;
using Microsoft.Data.Sqlite;
using SoccerSim.Core.Domain;
using SoccerSim.Core.Events;
using SoccerSim.Core.LifeSim;
using SoccerSim.Core.Localization;
using SoccerSim.Core.Random;
using SoccerSim.Core.Simulation;
using SoccerSim.Core.Time;
using SoccerSim.Core.World;
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

    /// <summary>
    /// The Career table is a singleton row (Id = 1), so life-sim state keys off this constant until
    /// a save file is allowed to hold more than one career.
    /// </summary>
    public const int ActiveCareerId = 1;

    public ITimeManager Time { get; private set; } = null!;

    public IEventManager Events { get; private set; } = null!;

    public ISimulationLODManager Lod { get; private set; } = null!;

    /// <summary>On-demand simulation of the player's rendered fixture (Tier 1 match scene).</summary>
    public IMatchPresenter Match { get; private set; } = null!;

    /// <summary>
    /// The off-pitch life simulation for the active career. Serves a player career and a manager
    /// career alike — <see cref="CareerState.Role"/> selects the need profile, not a separate service.
    /// </summary>
    public IWellbeingService Wellbeing { get; private set; } = null!;

    /// <summary>
    /// Resolves display text. Every string the player reads goes through here: the simulation only
    /// ever holds keys, so the code stays in English while the game runs in pt-BR.
    /// </summary>
    public ILocalizer Text { get; private set; } = null!;

    /// <summary>
    /// The world's locations. Backed by <see cref="TestWorldGazetteer"/> for now — the real world is
    /// authored in the City Searcher tool and swaps in behind this same port.
    /// </summary>
    public IWorldGazetteer World { get; private set; } = null!;

    /// <summary>Where the human currently is. Gates which activities the action bar offers.</summary>
    public WorldLocation CurrentLocation { get; private set; } = null!;

    /// <summary>Raised when <see cref="CurrentLocation"/> changes, so bound UI can re-filter.</summary>
    public event Action<WorldLocation>? LocationChanged;

    /// <summary>Move the human to a venue. Throws on an unknown id (fail-fast).</summary>
    public void TravelTo(string locationId)
    {
        CurrentLocation = World.Find(locationId)
            ?? throw new InvalidOperationException($"Unknown world location '{locationId}'.");
        LocationChanged?.Invoke(CurrentLocation);
    }

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

        Text = new Localizer(StringCatalogue.DefaultLocale);
        World = new TestWorldGazetteer();
        CurrentLocation = World.Find(World.HomeLocationId)!;

        Events = new EventManager(BuildEventDefinitions());
        Lod = new SimulationLODManager(_gateway, new ILeagueResolver[]
        {
            new Tier1MatchResolver(_rng),
            new Tier2EloResolver(_rng),
            new Tier3MathResolver(_rng),
        }, humanTeamId);
        Match = new MatchPresentationService(_gateway, new MatchEngine(_rng), humanTeamId);
        Wellbeing = BuildWellbeing();
        Time = new TimeManager(new GameClock(new DateTime(2026, 8, 1)), Events, Lod, BuildRollContext);

        // Each simulated day drains the human's needs. Subscribing here rather than inside
        // TimeManager keeps the core's time system unaware of the life-sim: time drives, the
        // life-sim consumes, exactly like the LOD manager does.
        Time.DayElapsed += OnDayElapsed;

        string human = Career is null ? "(none)" : $"player {Career.HumanPlayerId}, team {Career.HumanTeamId}";
        GD.Print($"[GameBootstrap] Core initialised. Save database: {databasePath}. " +
                 $"Human: {human}. Career role: {Wellbeing.Role}.");
    }

    /// <summary>
    /// Look up an event template by key so the resolution modal can render its prompt and choices.
    /// Throws when the key is unknown — a fired event with no definition is a wiring bug.
    /// </summary>
    public EventDefinition GetEventDefinition(string key) =>
        Events.Definitions.FirstOrDefault(definition => definition.Key == key)
        ?? throw new InvalidOperationException($"No event definition registered for key '{key}'.");

    public override void _ExitTree()
    {
        if (Time is not null)
            Time.DayElapsed -= OnDayElapsed;
        _connection?.Dispose();
    }

    /// <summary>
    /// Compose the life simulation for whichever career this save is running. A save with no career
    /// yet still gets a working (unpersisted) life-sim so the life-sim scene is explorable before a
    /// career exists.
    /// </summary>
    private IWellbeingService BuildWellbeing()
    {
        CareerRole role = Career?.Role ?? CareerRole.Player;

        if (Career is null)
            return new WellbeingService(WellbeingState.CreateDefault(role), new LifeSimulator(), _rng);

        var repository = new SqliteWellbeingRepository(_connection!);
        // Null means this career has never been advanced; seed the default gauges and write them
        // through so the very first day advances from a known state rather than an empty table.
        WellbeingState state = repository.Load(ActiveCareerId, role) ?? WellbeingState.CreateDefault(role);
        var service = new WellbeingService(state, new LifeSimulator(), _rng, repository, ActiveCareerId);
        repository.Save(ActiveCareerId, state);
        return service;
    }

    private void OnDayElapsed(DateTime date) => Wellbeing.AdvanceDay(date);

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

    /// <summary>
    /// Wellbeing feeds the roll here. <c>GlobalProbabilityMultiplier</c> already existed and
    /// <c>EventManager.RollForDay</c> already multiplies it into every probability, so a career that
    /// is falling apart attracts more life events without a single change to the event system.
    /// </summary>
    private EventRollContext BuildRollContext(DateTime date) =>
        new(Career?.HumanPlayerId ?? 1,
            Career?.TraitWeights ?? new Dictionary<string, int>(),
            Wellbeing.EventProbabilityMultiplier,
            _rng);

    // Definitions carry no prose: titles, prompts and choice labels are localisation keys derived
    // from the definition/choice keys (see LocKeys) and resolved against StringCatalogue at draw
    // time. That is what lets the game run in pt-BR while the code stays in English.
    private static IReadOnlyList<EventDefinition> BuildEventDefinitions() => new[]
    {
        new EventDefinition("contract_offer", EventTier.High, 0.01, new Dictionary<string, double>())
        {
            Choices =
            [
                new EventChoice("sign")
                {
                    ResourceDeltas = [new ResourceDelta("money", 250_000)],
                },
                new EventChoice("hold")
                {
                    StatDeltas = [new StatDelta("morale", -1)],
                },
                new EventChoice("walk")
                {
                    RequiredTraitKey = PlayerTraitWeights.Selfishness,
                    RequiredTraitWeight = 60,
                    StatDeltas = [new StatDelta("morale", 1)],
                },
            ],
        },
        new EventDefinition("press_conference", EventTier.Medium, 0.03,
            new Dictionary<string, double> { ["aggression"] = 0.05 })
        {
            Choices =
            [
                new EventChoice("deflect"),
                new EventChoice("back_squad")
                {
                    StatDeltas = [new StatDelta("morale", 1)],
                },
                // Trait-gated: only a hot-headed character is offered the reply that starts a fire.
                new EventChoice("hit_back")
                {
                    RequiredTraitKey = PlayerTraitWeights.Aggression,
                    RequiredTraitWeight = 60,
                    StatDeltas = [new StatDelta("morale", 2)],
                },
            ],
        },
        new EventDefinition("flight_delay", EventTier.Low, 0.02, new Dictionary<string, double>())
        {
            LowStakesStatDeltas = new[] { new StatDelta("morale", -1) },
        },
    };
}
