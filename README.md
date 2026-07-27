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
|  Domain / Economy         |    |  implement Core's ports       |
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
| `src/SoccerSim.Core/` | Engine-agnostic core: `TimeManager`, `EventManager`, `SimulationLODManager`, domain models, persistence interfaces. |
| `src/SoccerSim.Infrastructure.Sqlite/` | SQLite implementation of the persistence ports + the migration runner. |
| `sql/` | Canonical, numbered, ANSI-portable migrations (embedded into the infra assembly). |
| `game/` | The Godot 4 C# project: autoloads + scenes. |
| `tests/SoccerSim.Core.Tests/` | Headless xUnit tests proving the core runs without the engine. |
| `docs/ARCHITECTURE.md` | Design notes + the three required design artifacts (interfaces, schema, EventTrigger pseudo-code). |
| `docs/GAME_READY_PLAN.md` | Phased plan to make the game presentation-ready on Godot-native systems (display/theme, editor scenes, rendered match, life-sim, export). |
| `docs/LISTA_DE_ASSETS.md` | Asset production brief (PT-BR) for stage 1: every file to produce with exact dimensions, media formats, audio specs and acceptance criteria. |

## Build & run

Prerequisites: **.NET 8 SDK** and **Godot 4.6 (.NET/Mono build)**.

```bash
# 1. Headless: build + run the core/infra/test suite (no Godot needed).
dotnet test tests/SoccerSim.Core.Tests

# 2. Build everything via the solution.
dotnet build SoccerDreamGame.sln

# 3. Open the game in the Godot 4.6 (.NET) editor.
#    Import the `game/` folder, then Run. The autoloads create user://save.db,
#    apply the SQL migrations, and wire the core services on first launch.
```

## Persistence & the road to multiplayer

SQLite is embedded and perfect for the single-player game today. Because **all data
access goes through repository interfaces** in `SoccerSim.Core/Persistence` and the SQL
is kept ANSI-portable, moving to an online backend later (PostgreSQL, or a distributed
SQLite layer such as **Turso/libSQL**) means adding a sibling infrastructure project and
swapping the connection factory — **not** rewriting game logic.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design.
