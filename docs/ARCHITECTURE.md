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

## Off-pitch life simulation (one system, two career roles)

The life-sim lives in `SoccerSim.Core/LifeSim` with zero Godot dependencies. Its governing
decision: **a player career and a manager career share one simulation**, not two.

Six needs — `Energy`, `Nutrition`, `Fitness`, `Morale`, `Social`, `Focus` — drain daily for
whoever the human is. A manager still sleeps, eats and needs company. Exactly two things
change with [`CareerRole`](../src/SoccerSim.Core/LifeSim/CareerRole.cs):

1. **The tuning.** [`NeedProfile`](../src/SoccerSim.Core/LifeSim/NeedProfile.cs) supplies a
   per-need decay rate and weight. An athlete's profile weights `Fitness` heaviest; a manager's
   weights `Focus` and `Social`. Both profiles' weights **sum to 1.0** (enforced at construction,
   fail-fast) so a wellbeing index of 60 means the same thing in either career.
2. **Which derived output the consumer reads.** Every field is computed for every role.

Fame/reputation is deliberately **not** a need: it accumulates rather than draining toward a
deficit, so a bar that can never be topped up by resting would misteach the loop. It belongs with
the economy/standing systems.

### The needs are not decorative

[`WellbeingSnapshot`](../src/SoccerSim.Core/LifeSim/WellbeingSnapshot.cs) is the single read
point — nothing outside the life-sim inspects raw gauges. Each field lands in a system that
**already existed**, with no signature changes:

| Snapshot field | Consumer | Effect |
| --- | --- | --- |
| `FormModifier` (−5..+5) | `Player.FormMood` | `EffectiveAttributes` already applies FormMood, so wellbeing reaches the pitch with no match-engine change. Arcade mode (`applyForm: false`) still bypasses it. |
| `EventProbabilityMultiplier` | `EventRollContext.GlobalProbabilityMultiplier` | `EventManager.RollForDay` already multiplies this into every probability, so a struggling career attracts more life events. Anchored so index 75 ⇒ ×1.0, clamped to ×0.75..×1.75. |
| `InjuryRisk` | training / match layer (player) | Driven by `Fitness` + `Energy` deficits. |
| `DecisionQuality`, `BurnoutRisk` | dugout layer (manager) | Driven by `Focus`, `Energy`, `Morale`. |

### Determinism

`LifeSimulator.AdvanceDay` is two ordered passes and takes `IRandom` like the rest of the
simulation. Decay variance is one draw per need in `Needs.All` order; **cross-effects judge the
bands captured before any decay was written**, so a bottomed-out need drags its dependent down
(`Nutrition→Fitness`, `Energy→Focus`, `Social→Morale`) without the result ever depending on
iteration order. Same seed ⇒ same needs, pinned by `LifeSimTests`.

`ITimeManager` stays unaware of all this: `GameBootstrap` subscribes `DayElapsed` and calls
`AdvanceDay`. Time drives, the life-sim consumes — the same relationship the LOD manager has.

### Persistence

Migration [`0006_lifesim.sql`](../sql/0006_lifesim.sql) adds `Career.Role` (defaulted to
`'Player'`, so existing saves migrate) plus `CareerWellbeing` and `LifeActivityLog`, both keyed by
**career** rather than by player so a manager save uses the same schema. Gauges are stored
long-form (one row per need) so adding a seventh need is a data change, not a migration. Need keys
round-trip as enum *names*, so reordering `NeedKind` can never reinterpret a saved gauge. A
partially-saved state throws rather than letting a missing gauge default to zero and read as a
critical deficit the human never earned.

## Interface layer

The UI is Godot `Control` nodes plus a theme built in code from
[`UiTokens`](../game/ui/UiTokens.cs) — see [`docs/UI_DESIGN_SYSTEM.md`](UI_DESIGN_SYSTEM.md) for
the scales and the reasoning. Three pieces matter architecturally:

- **`HudNode`** hosts the persistent status strip on its own `CanvasLayer`, so it survives
  `ChangeSceneToFile`. Without that, the human loses the simulation state at every mode switch.
- **`EventResolutionDialog`** closes the interrupt/resume loop: `AdvanceCalendar` freezes the
  clock and emits a resume token, and this is where that token is answered. It is hosted on the
  event bus's own `CanvasLayer` (an interrupt can fire mid-scene-change) and is dismissal-proof
  (an unanswered event would stall the calendar). `EventChoice.RequiredTraitKey` supplies the
  trait gate that makes an `EventTier.Medium` event the trait-gated dialogue its tier promises.
- **`NeedsPanel` / `ActivityBar`** bind to `IWellbeingService` and read the career role from it,
  so one life-sim scene serves both careers.

## Verification

- `dotnet test tests/SoccerSim.Core.Tests` exercises the clock multipliers, task skip,
  the calendar advance, the trait-weighted roll, the High-event **interrupt → resume**
  cycle, the Tier 1/2/3 resolvers, the Tier 1 **minute-by-minute `MatchEngine`**
  (determinism, scoreline/scorer invariants, attribute-weighted finishing), the
  **life-sim** (role-tuned decay, order-independent cross-effects, seeded replay, the
  snapshot → FormMood → `EffectiveAttributes` chain, and the snapshot →
  `EventRollContext` chain), and an end-to-end SQLite migration + LOD write.
- `dotnet build SoccerDreamGame.sln` builds all four projects.
- Opening `game/` in the Godot 4.6 (.NET) editor and running creates `user://save.db`,
  applies migrations, and prints the bootstrap/autoload log lines.
