# Architecture & Foundational Design

This document captures the foundational structures that initialize the project and maps
them back to the Game Design Document (GDD). It contains the three required design
artifacts:

1. The core manager interfaces (`TimeManager`, `EventManager`, `SimulationLODManager`).
2. The SQL table schemas for `Players`, `Teams`, `Leagues`, `PlayerTraits`.
3. The `EventTrigger` loop pseudo-code that interrupts the calendar simulation.

---

## Layering & dependency rules

- **`SoccerSim.Core`** depends on nothing (no Godot, no NuGet). It holds the managers,
  domain models, and **persistence _ports_** (interfaces).
- **`SoccerSim.Content`** depends on Core only (no Godot, no database, no NuGet). It holds
  the authored-content model, the manifest, and the **validator** — the single definition of
  "is this content usable?", shared by the authoring tool and the game.
- **`SoccerSim.Infrastructure.Sqlite`** depends on Core + Content + `Microsoft.Data.Sqlite`
  and _implements_ the ports. It is the only database-aware project.
- **`tools/`** (ContentStudio, ContentCli) depend on Content + Infrastructure. Nothing depends
  on them.
- **`game/`** (Godot) depends on both, but only through interfaces. Godot code is
  confined to thin autoload wrappers and scenes.
- Dependencies point **inward**: Godot → Core ← Infrastructure. Nothing points at Godot.

This is what lets the calendar/background simulation run with rendering bypassed
(GDD §2) and lets a future multiplayer server reuse the core unchanged (GDD §7).

---

## 1. Core manager interfaces

Full source: [`src/SoccerSim.Core/Time/ITimeManager.cs`](../src/SoccerSim.Core/Time/ITimeManager.cs),
[`Events/IEventManager.cs`](../src/SoccerSim.Core/Events/IEventManager.cs),
[`Simulation/ISimulationLODManager.cs`](../src/SoccerSim.Core/Simulation/ISimulationLODManager.cs).

```csharp
public interface ITimeManager                    // GDD §2 Time Management
{
    DateTime CurrentDate { get; }
    TimeSpeed Speed { get; }                      // Paused / Normal / Double / Quadruple
    bool IsCalendarSimulating { get; }

    void SetSpeed(TimeSpeed speed);               // pause / 2x / 4x
    void Tick(double realDeltaSeconds);           // driven by Godot _Process(delta)
    TimeAdvanceResult SkipToTaskCompletion(DateTime taskEnd);            // task skipping
    TimeAdvanceResult AdvanceCalendar(DateTime target, CancellationToken ct = default);
    TimeAdvanceResult ResumeCalendar(Guid resumeToken, DateTime target, CancellationToken ct = default);

    event Action<DateTime>? DayElapsed;           // LOD + form hooks
    event Action<DateTime>? WeekElapsed;
    event Action<TimeAdvanceResult>? AdvanceInterrupted;
}

public interface IEventManager                    // GDD §4 Event & Interruption System
{
    IReadOnlyList<EventDefinition> Definitions { get; }
    GameEvent? RollForDay(DateTime date, EventRollContext context);     // RNG vs thresholds
    EventResolutionResult ResolveLowStakes(GameEvent gameEvent);        // unavoidable, inline
    Task<EventResolutionResult> RequestResolutionAsync(GameEvent gameEvent, Guid resumeToken);
    event Func<EventResolutionRequest, Task<EventResolutionResult>>? ResolutionRequested;
}

public interface ISimulationLODManager            // GDD §6 Simulation Level of Detail
{
    SimulationTier GetTier(int leagueId);         // ActiveHuman(1) / MajorForeign(2) / Minor(3)
    void OnDayElapsed(DateTime date);             // Tier 1 daily
    void OnWeekElapsed(DateTime weekEnd);         // Tier 2 weekly, Tier 3 week-end
    MatchResult ResolveMatch(int matchId, SimulationTier tier);
}
```

**How they interconnect.** `TimeManager.AdvanceCalendar` is the driver. For each
simulated day it pushes `DayElapsed`/`WeekElapsed` to the `SimulationLODManager`
(the consumer, which resolves Tier 1/2/3 matches and writes rows) and pulls a roll from
`EventManager` (the interrupter). A High/Medium event bubbles out as an interrupted
`TimeAdvanceResult` carrying a resume token; the Godot `EventBus` resolves it via a
scene and calls `ResumeCalendar`. Low-stakes events resolve inline and the loop keeps
going. **Time drives, LOD consumes, Events veto & hand back a token.**

### Static vs dynamic character state (GDD §3)

- **Static** (assigned at generation, read-only in season): `Player.BaseAttributes`,
  `Player.Traits` (`PlayerTrait` dictates event weights _and_ on-pitch AI).
- **Dynamic** (recalculated, reset seasonally, bypassed in arcade): `FormMood` (−5..+5),
  applied via `PlayerAttributes.WithModifier`. `Player.EffectiveAttributes(applyForm)`
  is the single read point; arcade modes pass `applyForm: false`.

---

## 2. SQL table schemas

Canonical source: [`sql/0001_initial_schema.sql`](../sql/0001_initial_schema.sql) and
siblings. The four GDD-named tables (abridged — see the file for full constraints):

```sql
CREATE TABLE Leagues (
    Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Country TEXT NOT NULL,
    Tier INTEGER NOT NULL CHECK (Tier IN (1,2,3)),   -- LOD tier
    CurrentSeasonId INTEGER NULL
);

CREATE TABLE Teams (
    Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, LeagueId INTEGER NOT NULL,
    Budget INTEGER NOT NULL DEFAULT 0, EloRating INTEGER NOT NULL DEFAULT 1500,
    FOREIGN KEY (LeagueId) REFERENCES Leagues (Id) ON DELETE CASCADE
);

CREATE TABLE Players (                              -- STATIC base attributes (1..20)
    Id INTEGER PRIMARY KEY, FirstName TEXT NOT NULL, LastName TEXT NOT NULL,
    TeamId INTEGER NULL,
    Pace INTEGER NOT NULL CHECK (Pace BETWEEN 1 AND 20), /* Stamina, Strength, Passing,
    Shooting, Tackling, Vision — all CHECK 1..20 */
    FOREIGN KEY (TeamId) REFERENCES Teams (Id) ON DELETE SET NULL
);

CREATE TABLE PlayerTraits (                         -- STATIC personality catalogue
    Id INTEGER PRIMARY KEY, Key TEXT NOT NULL UNIQUE, DisplayName TEXT NOT NULL,
    Aggression INTEGER NOT NULL CHECK (Aggression BETWEEN 0 AND 100),
    Selfishness INTEGER NOT NULL CHECK (Selfishness BETWEEN 0 AND 100),
    EventWeightBias INTEGER NOT NULL DEFAULT 0
);
-- + PlayerTraitAssignments (M:N), Seasons, Matches, Goals, Standings,
--   FormMood (dynamic, keyed by PlayerId+SeasonId), and the §5 economy tables.
-- 0006 adds a stable Key to every authored table plus the ContentBuilds stamp;
-- 0007 adds Nations, Stadiums, Competitions, Coaches and Contracts.
```

**On `Leagues`.** The table is a **division**, not a tournament: `Matches.LeagueId` points at
it and `Leagues.Tier` drives the entire LOD path (`SqliteFixtureGateway`,
`SimulationLODManager`). Migration 0007 therefore added `Competitions` as a *parent* rather
than renaming `Leagues` — renaming would have put the tournament's name on the routing unit.
A cup competition owns exactly one division so its fixtures still have a `LeagueId` to route
on; that invariant spans two tables, so the content validator enforces it, not SQL.

**Portability:** `INTEGER PRIMARY KEY` (rowid alias), ISO-8601 `TEXT` timestamps, and
`INTEGER` booleans all map 1:1 to PostgreSQL. Only the connection factory + identity
DDL change when migrating; queries (isolated behind repositories) do not.

---

## 3. EventTrigger loop pseudo-code

This is the loop that runs during background calendar simulation. It is implemented for
real in [`TimeManager.AdvanceCalendar`](../src/SoccerSim.Core/Time/TimeManager.cs) and
[`EventManager.RollForDay`](../src/SoccerSim.Core/Events/EventManager.cs).

```text
function AdvanceCalendar(target):
    resumeToken   = newGuid()
    speedBefore   = (Speed == Paused) ? Normal : Speed
    IsCalendarSimulating = true
    Speed = Normal

    while CurrentDate < target:
        if cancellationRequested: break

        nextDay = min(CurrentDate.Date + 1 day, target)
        CurrentDate = nextDay

        # 1) Level-of-detail resolution (pure math + SQL writes, NO assets loaded)
        raise DayElapsed(nextDay)            -> LOD.OnDayElapsed   (Tier 1 fixtures)
        if nextDay is week boundary:
            raise WeekElapsed(nextDay)       -> LOD.OnWeekElapsed  (Tier 2 + Tier 3)

        # 2) Roll for an interrupting life-sim event (Tier 1 active league only)
        context = buildRollContext(activePlayer)   # trait weights + global mult + RNG
        event   = EventManager.RollForDay(nextDay, context)
        if event == null: continue

        if event.Tier == Low:
            EventManager.ResolveLowStakes(event)    # unavoidable modifier, applied inline
            continue                                # keep advancing, no scene change

        # High / Medium: INTERRUPT — freeze the clock and hand control to the UI
        Speed = Paused
        IsCalendarSimulating = false
        raise AdvanceInterrupted(result{Interrupted, event, resumeToken})
        return result                               # caller shows scene, then ResumeCalendar

    # reached target with no pending High/Medium event
    IsCalendarSimulating = false
    Speed = speedBefore
    return result{Completed, reached = CurrentDate, resumeToken}


function RollForDay(date, context):                 # inside EventManager
    for definition in Definitions ordered High -> Low:
        p = definition.BaseProbability * context.GlobalProbabilityMultiplier
        for (traitKey, modifier) in definition.TraitModifiers:     # personality weighting
            if context.TraitWeights has traitKey:
                p += (TraitWeights[traitKey] / 100) * modifier
        p = clamp(p, 0, 1)
        if context.Rng.NextDouble() < p:
            return new GameEvent(newGuid(), definition.Key, definition.Tier, date, playerId)
    return null


function ResumeCalendar(resumeToken, target):       # after the UI resolved the event
    if resumeToken != activeResumeToken: throw       # stale token guard
    return AdvanceCalendar-style loop continuing from CurrentDate to target
```

The resume path continues from the day the event fired (that day's LOD + roll already
ran), so no day is processed twice.

---

## Orchestration: modes, time control & determinism

The "upstairs" orchestration layer decides *which mode runs* and *how randomness is seeded*.
It follows the same golden rule as the rest of the project — logic in the engine-agnostic core,
a thin Godot autoload on top.

- **Mode state machine** — [`Modes/ModeStateMachine`](../src/SoccerSim.Core/Modes/ModeStateMachine.cs)
  is the single authority over the active [`GameMode`](../src/SoccerSim.Core/Modes/GameMode.cs)
  (`Loading` hub · `Calendar` · `LifeSim` · `Match`). Exactly one mode is active; transitions are an
  explicit table and any unlisted transition **throws** (fail-fast, no silent snap-back). The Godot
  autoload [`GameModeManager`](../game/autoload/GameModeManager.cs) wraps it to drive the scene swap
  (`GetTree().ChangeSceneToFile`) and the orchestration-level time controls — `Pause()`/`Resume()`
  (`GetTree().Paused`, resetting `Engine.TimeScale` to 1 first to avoid the known pause jitter) and
  `SetFastForward(scale)` (`Engine.TimeScale`). It is the **one** way scenes change, and runs with
  `ProcessMode = Always` so it survives the pause. Entering a match runs the pure
  [`MatchEntryGuard`](../src/SoccerSim.Core/Modes/MatchEntryGuard.cs): a null, already-played, or
  malformed fixture (missing/duplicate clubs) **throws**.

- **Determinism** — all simulation randomness flows through `IRandom`, whose single production
  implementation is now [`SplitMix64Random`](../src/SoccerSim.Core/Random/SplitMix64Random.cs): a
  pure-C# SplitMix64 PRNG that is bit-for-bit reproducible across platforms and .NET versions. The
  old `System.Random`-backed `SeededRandom` is gone — `System.Random`'s sequence is unspecified, so
  it cannot back a deterministic save. [`DeterministicRng.CreateStream(masterSeed, …keys)`](../src/SoccerSim.Core/Random/DeterministicRng.cs)
  derives **isolated** child streams via hash mixing, enabling hierarchical seeding such as
  `hash(masterSeed, fixtureId, season, round)` so a single match can be re-simulated in isolation.
  `GameBootstrap` holds the world `MasterSeed` and seeds the shared generator from it.

The per-tier match resolvers already are the match-resolution extension point: the
[`SimulationLODManager`](../src/SoccerSim.Core/Simulation/SimulationLODManager.cs) routes each
fixture to exactly one `ILeagueResolver` by tier (Tier 1 minute-by-minute engine · Tier 2 Elo ·
Tier 3 pure math) and **throws** if a tier has no resolver — one route per tier, no fallback.

## Tactics → Behaviour (the player brain)

The on-pitch AI turns **team tactics into individual decisions** per tick. It lives entirely in
`SoccerSim.Core` (namespaces `Tactics`, `Ai`, `Pitch`) with zero Godot dependencies — steering
math uses the engine-agnostic [`Vec2`](../src/SoccerSim.Core/Pitch/Vec2.cs) (double-precision,
deterministic); the game layer converts to `Godot.Vector2` at the rendering boundary.

Two layers, one weight source:

- **Movement** — [`SteeringBehaviors`](../src/SoccerSim.Core/Ai/Steering/SteeringBehaviors.cs)
  (Reynolds: `Arrive`, `Pursue`, `Interpose`, `Separation`, weighted `Blend`), anchored to the
  formation slot via [`TacticalAnchor`](../src/SoccerSim.Core/Ai/TacticalAnchor.cs). Runs every tick.
- **Decision** — [`UtilityDecider`](../src/SoccerSim.Core/Ai/Utility/UtilityDecider.cs): response-curve
  considerations score a lean action set (short/long pass, shoot, carry, dribble; tackle/contain)
  for whoever has or contests the ball. Re-evaluated every `TacticalPlayerBrain.UtilityReevaluationTicks`
  (8 ticks ≈ 7.5 Hz at the 60 Hz tick rate) with a current-action bonus — **hysteresis is mandatory**,
  otherwise near-tied scores make players twitch. Ties break through `IDeterministicRandom`.
- **The single modulation point** — [`BehaviourWeights.Derive`](../src/SoccerSim.Core/Ai/BehaviourWeights.cs)
  combines [`TeamTactics`](../src/SoccerSim.Core/Tactics/TeamTactics.cs) (one 4-4-2
  [`Formation`](../src/SoccerSim.Core/Tactics/Formation.cs), `Mentality`, normalised
  `TeamInstructions`), the `PersonalityProfile` stub, and role/duty into the weights BOTH layers
  consume. Tactics tilt weights; behaviour emerges. Invalid tactics **throw** (fail-fast).
- **Shared spatial picture** — a zonal [`InfluenceMap`](../src/SoccerSim.Core/Ai/InfluenceMap.cs)
  (12×8 grid) both layers consult for space/control queries.

[`TacticalPlayerBrain`](../src/SoccerSim.Core/Ai/TacticalPlayerBrain.cs) implements the
`IPlayerBrain.Decide(tick, perception) → Intention` contract and is instantiated per player by
[`PitchSimulation`](../src/SoccerSim.Core/Pitch/PitchSimulation.cs) — the minimal tick host
(player/ball state, pass/shot/tackle execution, first-touch windows, a sweeping keeper). The
minute-by-minute `MatchEngine` remains the Tier 1 background resolver; the tick simulation is the
substrate for the rendered/playable match scene (~22 brains, bounded by LOD). Acceptance tests in
`TacticalBrainTests`/`PitchSimulationTests` pin the module's contract: identical seeds ⇒ identical
trajectories, low decision-switch rates, and tactics measurably changing behaviour (pressing ⇒
closer defenders in build-up, directness ⇒ longer passes, mentality ⇒ higher anchors, selfishness
⇒ shooting over passing).

## Content pipeline

Game content is **authored data, not code**. Until migration 0006 the entire world lived in a
hand-written `sql/9999_seed_dev.sql`; that file is gone, and the world now comes from a
versioned JSON bundle in `content/dev/`.

```
  Content Studio  ->  content/dev/*.json  ->  validate  ->  build/content/content.db
   (browser CRUD)      committed to git       shared          playable artifact
                       = source of truth      validator
                              |
                              +--> embedded into game.dll --> imported into user://save.db
```

- **The bundle** ([`ContentBundle`](../src/SoccerSim.Content/ContentBundle.cs)) is one JSON
  file per category plus a [`ContentManifest`](../src/SoccerSim.Content/ContentManifest.cs)
  carrying format/content versions and a SHA-256 over the canonical payload. The hash is
  computed over the **JSON**, never over the generated `.db` — SQLite page layout varies
  between writes, so an identical build would otherwise look changed.
- **Identity.** Every entity carries a stable numeric `Id` *and* a textual `Key`. Ids are what
  save-file foreign keys point at, so they must never shift; keys are what the JSON
  cross-references, because numbers make a merge unreadable. Deriving ids from sorted key
  position was considered and rejected — inserting one entity would renumber everything after
  it, silently repointing `Matches`, `Standings` and `Career` in existing saves.
- **Static vs save state.** The bundle carries authored content (nations, competitions, clubs,
  players, coaches, contracts, traits, housing items) plus a `world` section holding the
  *initial* seasons, fixtures, balances and career — the starting position a new save is built
  from. Once a career is running, the game owns those tables.
- **One validator.** [`ContentValidator`](../src/SoccerSim.Content/Validation/ContentValidator.cs)
  is called by the authoring tool while you type, by `contentcli validate` in CI, and again by
  [`SqliteContentImporter`](../src/SoccerSim.Infrastructure.Sqlite/Content/SqliteContentImporter.cs)
  before it touches the database. It **gates the build, not each keystroke** — a half-entered
  squad must stay editable, so "fewer than 11 players" is a warning while a dangling reference
  is an error. Rules carry stable codes (`REF_DANGLING`, `ID_DUPLICATE`, `CONTRACT_OVERLAP`, …)
  that the UI links to and the tests assert on.
- **Version skew.** A bundle declaring a newer format or content version than the running build
  **throws** with an actionable message rather than loading a half-understood world — the same
  fail-fast rule as `MatchEntryGuard` and the tactics validation.
- **Shipping.** The bundle is an **embedded assembly resource**, not a `res://` file: in an
  exported Godot build `res://` lives inside the `.pck` and is not a real path, so a
  file-based load would work in the editor and fail once exported.
  `GameBootstrap._Ready()` migrates, then calls `EnsureImported`, which is a no-op once the
  save already holds that content hash.

## Verification

- `dotnet test tests/SoccerSim.Content.Tests` exercises the content pipeline: JSON and SQLite
  round-trips (the only proof the column mapping is lossless in both directions), every
  validator rule, the version-compatibility matrix, CSV round-trip and error reporting, and
  that the shipped bundle validates and builds the expected world.
- `dotnet test tests/SoccerSim.Core.Tests` exercises the clock multipliers, task skip,
  the calendar advance, the trait-weighted roll, the High-event **interrupt → resume**
  cycle, the Tier 1/2/3 resolvers, the Tier 1 **minute-by-minute `MatchEngine`**
  (determinism, scoreline/scorer invariants, attribute-weighted finishing), and an
  end-to-end SQLite migration + LOD write.
- `dotnet build SoccerDreamGame.sln` builds all four projects.
- `dotnet run --project tools/SoccerSim.ContentCli -- validate` checks the shipped bundle and
  that its manifest hash still matches the files on disk.
- Opening `game/` in the Godot 4.6 (.NET) editor and running creates `user://save.db`,
  applies migrations, imports the embedded content build, and prints the bootstrap/autoload
  log lines including the content hash and per-category counts.
