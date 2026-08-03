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
|  Domain / Economy         |    |  content import/export        |
|  Persistence PORTS (iface)|    |  implement Core's ports       |
+---------------------------+    +-------------------------------+
            ^                                  ^
            |                                  |
+---------------------------+                  |
| src/SoccerSim.Content     |------------------+
| (bundle model, validator, |
|  JSON + CSV; no Godot,    |<--- tools/SoccerSim.ContentStudio (browser CRUD)
|  no database)             |<--- tools/SoccerSim.ContentCli    (headless)
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
| `src/SoccerSim.Content/` | The authored-content model: bundle, manifest, validator, JSON + CSV. Depends on Core only. |
| `src/SoccerSim.Infrastructure.Sqlite/` | SQLite implementation of the persistence ports, the migration runner, and the content importer/exporter. |
| `sql/` | Canonical, numbered, ANSI-portable **schema** migrations (embedded into the infra assembly). No content. |
| `content/dev/` | **The authored game world**, as versioned JSON. This is the source of truth for clubs, players, coaches and competitions. |
| `tools/SoccerSim.ContentStudio/` | Development-only browser CRUD for editing `content/` — see [`docs/CONTENT-STUDIO.md`](docs/CONTENT-STUDIO.md). |
| `tools/SoccerSim.ContentCli/` | Headless `validate` / `build` / `stats` / `reexport`, for CI. |
| `game/` | The Godot 4 C# project: autoloads + scenes. |
| `tests/SoccerSim.Core.Tests/` | Headless xUnit tests proving the core runs without the engine. |
| `tests/SoccerSim.Content.Tests/` | Tests for the content pipeline: validation, JSON/CSV/SQLite round-trips. |
| `docs/ARCHITECTURE.md` | Design notes + the three required design artifacts (interfaces, schema, EventTrigger pseudo-code). |
| `docs/CONTENT-STUDIO.md` | Step-by-step guide to the authoring tool (in Portuguese). |

## Run the game

Requires **.NET 8 SDK** and **Godot 4.6 (.NET/Mono build)** — this is the only step that
needs Godot.

Open the `game/` folder in the Godot 4.6 (.NET) editor and press Run. On first launch the
autoloads create `user://save.db`, apply the SQL migrations, import the embedded content
bundle, and wire the core services together.

## Develop

Everything below runs headlessly — no Godot involved.

```bash
dotnet build SoccerDreamGame.sln        # core + content + infra + tools + the Godot project
dotnet test tests/SoccerSim.Core.Tests  # the core runs without the engine
dotnet test tests/SoccerSim.Content.Tests
```

## Authoring content

Game content — clubs, players, coaches, competitions, stadiums, contracts — is **not**
written in SQL or C#. It lives in `content/dev/` as versioned JSON, edited through a local
browser tool that ships in this repo as a development utility (it is not part of the game
and is not distributed with it):

```bash
dotnet run --project tools/SoccerSim.ContentStudio     # then open http://127.0.0.1:5099
```

**→ [`docs/CONTENT-STUDIO.md`](docs/CONTENT-STUDIO.md) is the step-by-step guide** (in
Portuguese): the grid, bulk paste from a spreadsheet, what blocks a build, and — the part
that is easy to get wrong — how an edit actually reaches the running game.

How it fits together:

```
  Content Studio  ->  content/dev/*.json  ->  validate  ->  build/content/content.db
   (browser CRUD)      committed to git       shared          pre-flight check
                       = source of truth      validator       (not loaded by the game)
                              |
                              +--> embedded into game.dll --> imported into user://save.db
```

Note the two arrows out of the JSON. `content.db` proves the bundle imports cleanly into a
real schema; **the game loads the embedded JSON**, not that file. So a content edit reaches
the game by rebuilding, not by rebuilding `content.db`.

Because the JSON is embedded at compile time, **editing content requires rebuilding the
game**, and the previous save then holds a different content build. In a debug build the
game rebuilds that save automatically (and warns that the career is gone); a save with
played matches is never discarded — it fails the boot with the file to delete named in the
message. See `SaveDatabase.Open`.

Two rules make this safe. **The validator is shared with the game**, so the tool cannot
approve content the game would reject. And **entities carry both a stable numeric `id`**
(what save-file foreign keys point at) **and a textual `key`** (what the JSON
cross-references), so content merges across branches as readable text without ever
renumbering rows a save already depends on.

## Persistence & the road to multiplayer

SQLite is embedded and perfect for the single-player game today. Because **all data
access goes through repository interfaces** in `SoccerSim.Core/Persistence` and the SQL
is kept ANSI-portable, moving to an online backend later (PostgreSQL, or a distributed
SQLite layer such as **Turso/libSQL**) means adding a sibling infrastructure project and
swapping the connection factory — **not** rewriting game logic.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design.
