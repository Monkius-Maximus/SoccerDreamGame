# Soccer Dream Game

A **2D Soccer Life Simulator** that separates the action of a soccer match from an
off-pitch life simulation. Built on **Godot 4 (C#/.NET 8)** with an **engine-agnostic
simulation core** and **SQLite** persistence.

## Architecture at a glance

The golden rule: **all simulation logic is engine-agnostic and lives outside the Godot
project**, so the calendar/background simulation runs headlessly (and is unit-tested
without the engine), and a future multiplayer server can reuse the exact same core.

```
+-------------------------------------------------------------+
|  game/  (Godot 4 project — the ONLY thing that opens in     |
|         the editor; the only Godot.NET.Sdk project)         |
|   autoload/  thin Node wrappers that drive the core         |
|   scenes/    match (side-on) · lifesim (iso) · calendar     |
+----------------------------|--------------------------------+
                             | project references
            +----------------+-----------------+
            v                                  v
+---------------------------+    +-------------------------------+
| src/SoccerSim.Core        |    | src/SoccerSim.Infrastructure  |
| (no Godot, no deps)       |<---| .Sqlite (Microsoft.Data.Sqlite)|
|  Time / Events / Sim LOD  |    |  repositories + migrations    |
|  Domain / Economy/ LifeSim|    |  implement Core's ports       |
|  Persistence PORTS (iface)|    +-------------------------------+
+---------------------------+
            ^
            | references Core only
+---------------------------+
| tests/SoccerSim.Core.Tests (xUnit, headless — no Godot)     |
+-------------------------------------------------------------+
```

Why the core lives in `src/` (a sibling of `game/`, not inside it): the `Godot.NET.Sdk`
globs every `.cs` file under the Godot project directory, so a core kept inside `game/`
would be compiled twice. Keeping it in `src/` avoids that.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/SoccerSim.Core/` | Engine-agnostic core: `TimeManager`, `EventManager`, `SimulationLODManager`, the `LifeSim` needs system, domain models, persistence interfaces. |
| `src/SoccerSim.Infrastructure.Sqlite/` | SQLite implementation of the persistence ports + the migration runner. |
| `sql/` | Canonical, numbered, ANSI-portable migrations (embedded into the infra assembly). |
| `game/` | The Godot 4 C# project: autoloads + scenes. |
| `game/ui/` | The design system (`UiTokens`, `UiTheme`) and shared controls (HUD strip, needs panel, event modal). |
| `tests/SoccerSim.Core.Tests/` | Headless xUnit tests proving the core runs without the engine. |
| `docs/ARCHITECTURE.md` | Design notes + the three required design artifacts (interfaces, schema, EventTrigger pseudo-code). |
| `docs/UI_DESIGN_SYSTEM.md` | Interface tokens, contrast/type rules, and what changed from the UI concept. |
| `docs/WORLD_INTEGRATION.md` | The City Searcher seam and the ordered merge sequence for the real world. |
| `docs/ROADMAP.md` | Sprint breakdown, technical debt register, and what is out of scope. |

## Build & run

Prerequisites: **.NET 8 SDK** and **Godot 4.6 (.NET/Mono build)**.

```bash
# 1. Headless: build + run the core/infra/test suite (no Godot needed).
dotnet test tests/SoccerSim.Core.Tests

# 2. Build everything via the solution.
dotnet build SoccerDreamGame.sln
```

Then import the **`game/`** folder in the Godot 4.6 (.NET) editor and press Build, then Run. The
autoloads create `user://save.db`, apply the SQL migrations, and wire the core services on first
launch.

### If the window is blank

The symptom of a C# assembly that failed to load is a black window with no obvious error: every
script silently detaches from its node, so no autoload runs and the main scene renders as an empty
`Control`. Two things cause it, and both are invisible to `dotnet build`:

1. **Assembly-name mismatch.** `game/game.csproj`'s `<AssemblyName>` MUST equal
   `[dotnet] project/assembly_name` in `project.godot` (both are `SoccerDreamGame`). Without an
   explicit `<AssemblyName>`, MSBuild names the assembly after the project *file* — `game.dll` —
   and Godot looks for `SoccerDreamGame.dll` and finds nothing.
2. **Stale build output.** Godot builds into `game/.godot/mono/temp/bin/<Config>/`, which is
   git-ignored. After changing the assembly name, or when switching branches, an old DLL can linger.
   Delete `game/.godot/mono/` and rebuild.

Check Godot's **Output** panel on launch: a healthy start prints `[GameBootstrap] Core initialised.`
followed by the other autoloads. If those lines are absent, the assembly did not load — it is not a
scene or UI problem.

### Where to look once it runs

The hub menu offers **Viver o Dia a Dia** (the life-sim: needs, activities, travel),
**Jogar a Próxima Partida**, and **Avançar o Calendário**. In-game, `P` opens the phone and `Esc`
opens the quick menu (where the career role can be switched).

## Off-pitch life simulation

Eight needs — Energy, Nutrition, Hygiene, Fitness, MuscleCondition, Morale, Social, Focus — drain
each simulated day for whoever the human controls. **A player career and a manager career share one
simulation**, not two: the needs are identical (a manager still sleeps, eats and needs company) and
only the `NeedProfile` tuning differs — an athlete's life weights conditioning and muscle freshness,
a manager's weights clarity and the dressing room.

The gauges are load-bearing, not cosmetic. `WellbeingSnapshot` is the single read point, and its
fields feed systems that already existed: form (and therefore effective attributes) via
`Player.FormMood`, life-event pressure via `EventRollContext`, plus injury risk for a player and
decision quality for a manager.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#off-pitch-life-simulation-one-system-two-career-roles)
for the wiring and [`docs/UI_DESIGN_SYSTEM.md`](docs/UI_DESIGN_SYSTEM.md) for the interface tokens.

## Persistence & the road to multiplayer

SQLite is embedded and perfect for the single-player game today. Because **all data
access goes through repository interfaces** in `SoccerSim.Core/Persistence` and the SQL
is kept ANSI-portable, moving to an online backend later (PostgreSQL, or a distributed
SQLite layer such as **Turso/libSQL**) means adding a sibling infrastructure project and
swapping the connection factory — **not** rewriting game logic.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design.
