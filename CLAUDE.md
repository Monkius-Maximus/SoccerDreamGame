# CLAUDE.md — SoccerDreamGame

Context for AI agents working in this repository. Read this first, then the ADR that covers
the area you are touching (`docs/adr/`).

## What this repo is

Two things share one C#/.NET 8 codebase:

1. **The game** — a 2D soccer life simulator in Godot 4.6 (.NET), with an engine-agnostic
   simulation core and SQLite persistence. See `README.md` and `docs/ARCHITECTURE.md`.
2. **The World Builder** (`tools/SoccerSim.WorldBuilder`, "Ferramenta de Mundo") — the
   authoring tool for the Terra Paralela world: clubs (ClubIdentity v2), squads
   (CharacterRecord), geography (GeoNode), competitions, pyramids, calibration. It replaced
   an 11-tab spreadsheet and **is the source of truth for world data** (ADR-0001 D-03). The
   spreadsheet/CSV is an export (and a guest import with a diff first, ADR-0006).

## Layout

| Path | What lives there |
| --- | --- |
| `src/SoccerSim.Core/` | Engine-agnostic core. No Godot, no NuGet. All domain logic. |
| `src/SoccerSim.Core/World/` | World-authoring domain: validation, economy, generation, colour math, import/export, projection, history. |
| `src/SoccerSim.Infrastructure.Sqlite/` | The only database-aware project; implements Core's persistence ports. |
| `sql/` | Numbered, ANSI-portable migrations, embedded in the infra assembly. `9999_seed_dev.sql` is optional dev seed. |
| `game/` | Godot project (`Godot.NET.Sdk/4.6.3`): thin autoloads + scenes only. |
| `tools/SoccerSim.WorldBuilder/` | ASP.NET Core Minimal API (`Api/*Endpoints.cs`) + static UI in `wwwroot/` (`index.html`, `app.js`, `app.css`, vanilla JS). |
| `tests/SoccerSim.Core.Tests/` | Headless xUnit for core + infra. `TestData/world.json` and `gen_profiles.json` are the canonical world and generation profiles. |
| `tools/SoccerSim.WorldBuilder.Tests/` | xUnit + `WebApplicationFactory` API tests and a shallow front-end smoke test. |
| `docs/adr/` | One ADR per decision (0001–0011). Read the relevant one before changing behaviour. |
| `docs/ROADMAP-GENERATION.md` | Current work: Sprints 10–14 (generating clubs, divisions, staff, free agents, career NPCs), per ADR-0011. |

## Hard rules

- **Dependencies point inward:** Godot → Core ← Infrastructure. Nothing references Godot.
  Core references nothing.
- **The World Builder is HTTP-thin.** Logic goes in `SoccerSim.Core/World/`, never in endpoint
  code or `app.js`. That is what lets the game trust the same logic later.
- **The World Builder stays a standalone project** until the game's core entities are settled;
  later it becomes a modding/customization layer. It is not a stopgap to "clean up"
  (ADR-0001 amendment).
- **Two schemas coexist on purpose** (ADR-0002): legacy `Teams/Players` (7 attrs, 1–20) used by
  `MatchEngine`, and the world schema (12 attrs, 1–99). The bridge is
  `World/Projection/WorldToLegacyProjection` (ADR-0005). Don't migrate `Simulation/` to the
  world schema.
- **Migrations:** add a new file with the next number in `sql/`. Don't rewrite one that has
  already shipped.

## World data rules

- **Field provenance** (`World/Fields/WorldField.cs`): `Authored` (a person chose it, needs a
  source or evidence), `Derived` (a documented rule over other fields), `Sampled` (drawn from a
  measured profile with a seed), `Calculated` (a formula, never stored by hand). Don't make a
  Derived or Calculated field hand-editable.
- **Anchored vs Regen:** a club or player derived from a real one is `Anchored` and carries a
  deviation audit. A generated one is `Regen` and has none. For clubs, `Provenance` is computed
  from `Audit` (null means Regen, ADR-0011 §2). Never fill an audit with placeholders.
- **Determinism:** SplitMix64 (`Core/Random/SplitMix64Random.cs`), with
  `seed = hash(masterSeed, StreamName, entityId)` and `masterSeed = 20260814`. Stream keys use
  `StableHash.Of(string)`, never `string.GetHashCode()`. The same seed must
  reproduce the same output.
- **Rounding:** round the weighted sum to 9 places, then half-up
  (`MidpointRounding.AwayFromZero`). C#'s default banker's rounding is wrong here.
- **Calibration is data, not literals:** constants live in `WorldCalibration`, e.g.
  `deltaEThreshold = 25` (ADR-0001 D-04, provisional until a renderer exists).
- **Closed enums fail fast:** a value outside the vocabulary throws. Imports are
  all-or-nothing and report one message per bad record.
- **CSV dialect** (ADR-0006): `;` separator, `|` for lists, comma decimals. A dot decimal is an
  error, not a fallback.
- **IP hygiene:** audit data (`audit`, `Audit_Clubes`, `Audit_Jogadores`) holds the real-world
  anchor names. It stays in the authoring side and must never reach a game build.

## Build, test, run

Prerequisites: .NET 8 SDK. Godot 4.6 .NET is only needed for `game/`.

```bash
dotnet build SoccerDreamGame.sln
dotnet test tests/SoccerSim.Core.Tests
dotnet test tools/SoccerSim.WorldBuilder.Tests

# World Builder: load the canonical world into a local SQLite db, then serve the UI
dotnet run --project tools/SoccerSim.WorldBuilder -- import tests/SoccerSim.Core.Tests/TestData/world.json
dotnet run --project tools/SoccerSim.WorldBuilder -- import-profiles tests/SoccerSim.Core.Tests/TestData/gen_profiles.json
dotnet run --project tools/SoccerSim.WorldBuilder            # then open the URL it prints
dotnet run --project tools/SoccerSim.WorldBuilder -- project  # rewrite legacy game tables from the world
```

`world.db` is created in the working directory and is gitignored (`*.db`). Schema migrations
run on startup, but data never does: a fresh checkout serves an empty world until you import.

## How to work here

- Before changing behaviour, read the ADR for that area. If you make a new decision, add
  `docs/adr/00NN-*.md` in the same style: context, decision, options considered, consequences.
- Put tests next to the change: Core logic → `tests/SoccerSim.Core.Tests`, endpoints →
  `tools/SoccerSim.WorldBuilder.Tests`. Both suites must pass before you finish.
- UI changes: keep the anchors that `FrontEndSmokeTests` checks (`rail-list`, `content`,
  `loading`). For visual changes, run the tool and look at the page. The smoke test is
  intentionally shallow.
- Keep changes small and surgical. No fallbacks and no silent coercion: throw when a
  precondition fails.
- Code, comments and ADRs are in English. The UI and user-facing strings are in pt-BR.
- Commit subjects follow the history, e.g. `World Builder Sprint 7: export and import — …`.

## Reference docs not yet in the repo

The ADRs cite `design_handoff_ferramenta_de_mundo/` (`ROADMAP.md`, `DATA_CONTRACT.md`,
`ALGORITHMS.md`, and the Claude Design prototype). Until those are committed under `docs/`,
treat the ADRs and the code as the authoritative description, and ask the owner if something
depends on those documents.
