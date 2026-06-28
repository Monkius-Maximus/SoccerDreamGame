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
- **`SoccerSim.Infrastructure.Sqlite`** depends on Core + `Microsoft.Data.Sqlite` and
  _implements_ the ports. It is the only database-aware project.
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
```

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

The per-tier match resolvers are the match-resolution extension point: the
[`SimulationLODManager`](../src/SoccerSim.Core/Simulation/SimulationLODManager.cs) routes each
fixture to exactly one `ILeagueResolver` by tier and **throws** if a tier has no resolver — one
route per tier, no fallback:

- **Tier 1** → [`Tier1MatchResolver`](../src/SoccerSim.Core/MatchEngine/Tier1MatchResolver.cs), which
  runs the deterministic **tick engine** ([`MatchSimulation`](../src/SoccerSim.Core/MatchEngine/MatchSimulation.cs))
  to completion. Each fixed tick runs one order — player decisions ([`IPlayerBrain`](../src/SoccerSim.Core/MatchEngine/IPlayerBrain.cs))
  → physics ([`BallPhysics`](../src/SoccerSim.Core/MatchEngine/BallPhysics.cs): gravity + drag +
  Magnus + wind + ground bounce) → geometric events (goal / out-of-play via the shared last touch)
  → referee judgement ([`IReferee`](../src/SoccerSim.Core/MatchEngine/IReferee.cs)) → emission.
  `Step` is the single advance gate, shared by the headless run and a future rendered observer.
  `PlaceholderPlayerBrain` + `NoOpReferee` are the stubs the real AI/officiating modules replace
  without touching the engine.
- **Tiers 2 & 3** → [`StatisticalMatchResolver`](../src/SoccerSim.Core/MatchEngine/StatisticalMatchResolver.cs):
  a **double-Poisson** model (λ from Elo-derived attack/defence ratings) sampled with deterministic
  Knuth [`PoissonSampler`](../src/SoccerSim.Core/MatchEngine/PoissonSampler.cs) — no tick loop. Tier 2
  produces ratings (weekly form); Tier 3 ignores form.

All three tiers emit the same `MatchResult`, so the orchestration consumes them identically. The
engine lives in namespace `SoccerSim.Core.MatchEngine` and uses a custom `double`-precision
`Vector3` (the core takes no Godot dependency); rendering is just an observer of the same engine.

## Verification

- `dotnet test tests/SoccerSim.Core.Tests` exercises the clock multipliers, task skip,
  the calendar advance, the trait-weighted roll, the High-event **interrupt → resume**
  cycle, the per-tier resolvers, the **ball physics** (side-spin curve, bounce energy loss,
  determinism), the deterministic **Poisson** sampler (mean ≈ λ), the **tick engine**
  (identical result + ball trajectory for a seed; fail-fast on an incomplete squad), and an
  end-to-end SQLite migration + LOD write.
- `dotnet build SoccerDreamGame.sln` builds all four projects.
- Opening `game/` in the Godot 4.6 (.NET) editor and running creates `user://save.db`,
  applies migrations, and prints the bootstrap/autoload log lines.
