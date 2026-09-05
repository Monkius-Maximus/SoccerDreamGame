# ADR-0001: World Authoring Tool — host, schema coexistence, source of truth, and calibration data

- Status: Accepted
- Date: 2026-09-05
- Source: `design_handoff_ferramenta_de_mundo/ROADMAP.md` ("Decisões pendentes — resolver antes
  do Sprint 1"), decided with the project owner before Sprint 1 started.

## Context

The project owner manages the world dataset (clubs, players, geography, competitions) for
Terra Paralela in an 11-tab spreadsheet (`TerraParalela_Liga_Brasileira_BaseDeMundo.xlsx`).
Assembling a single club's full picture requires cross-referencing all 11 tabs. A prototype
tool (Claude Design handoff, `design_handoff_ferramenta_de_mundo/`) was built to replace this
workflow: one page per club, guided authoring, comparison views, deterministic squad
generation. This ADR records the four decisions the roadmap flagged as blocking before any
code could be written, plus the reasoning.

## D-01 — Where the tool runs

**Decision: ASP.NET Core Minimal API + static HTML, in a new standalone project
`tools/SoccerSim.WorldBuilder`.**

The tool edits dense tables (20 columns in the comparison grid) and long forms (25+ fields per
club). Godot's UI toolkit is a poor fit for this, and a world-authoring tool has no reason to
be a runtime dependency of the game itself. Options considered:

| Option | Verdict |
| --- | --- |
| **ASP.NET Core Minimal API + static HTML (chosen)** | Reuses the HTML/CSS/JS prototype almost as-is; dense tables are what the web is good at; shares `SoccerSim.Core` with the game so validation/economy/generation logic is never duplicated. |
| Avalonia/WPF desktop | No server process, but the entire dense-table/form UI would need to be rebuilt from scratch instead of adapted from the prototype. |
| Godot editor plugin | Zero new projects, but couples authoring to the editor and is the worst option for tables/forms. |
| CLI + hand-edited JSON | Trivial to build, but is a rebrand of the problem the tool exists to solve (the user's original complaint: `.xlsx` tabs are hard to read). |

Project shape:

- `tools/SoccerSim.WorldBuilder` — the ASP.NET Core host. References `SoccerSim.Core` and
  `SoccerSim.Infrastructure.Sqlite`. Lives outside `game/` so Godot's file globbing never sees
  it (same reason `SoccerSim.Core` lives outside `game/`).
- `tools/SoccerSim.WorldBuilder.Tests` — headless xUnit, mirrors the `SoccerSim.Core.Tests`
  setup.
- All domain logic (validation, economy, squad generation, color math) lives in
  `SoccerSim.Core/World/`, not in the tool project. The tool is HTTP-thin by design: this is
  what lets the same logic be trusted by the game later.

**Explicit amendment to the roadmap's original recommendation, per the project owner
(2026-09-05):** the tool stays a **standalone** web project through the world-authoring
sprints (0–9 in `ROADMAP.md`). It is *not* folded into the main `SoccerDreamGame.sln` runtime
surface yet. Once the game's own core entities (teams, players, managers, and the other
gameplay-facing pieces in the GDD) are finalized, the tool is folded into the rest of the C#
application — but kept as a distinct **modding/customization layer** for the finished game,
not discarded or merged away. This ADR records that intent so a future sprint doesn't
"clean up" the standalone project as if it were a mistake: it is a deliberate staging decision,
not a stopgap.

## D-02 — Coexistence of the two schemas

**Decision: coexist, with a derived projection (`ROADMAP.md` Sprint 6), not a migration of
`Simulation/`.**

`main` today models `Teams(Name, Budget, EloRating)` and `Players` with 7 attributes on a
1–20 scale. The world-authoring schema (`ClubIdentity` v2 / `CharacterRecord`) models 12
attributes on a 1–99 scale, a geography hierarchy instead of a flat `Country` string, and a
much richer competition shape. These are two different models of the same domain, not a
mapping bug (`DATA_CONTRACT.md §1`).

Migrating `Simulation/` to the new model directly would be cleaner in the abstract, but
`MatchEngine` and `SimulationLODManager` are already tested and working against the legacy
shape. The chosen path — `Core/World/Projection/WorldToLegacyProjection` (Sprint 6) rewriting
`Teams`/`Players`/`Leagues` from the authored world — lets the tool exist and be useful
immediately without touching tested simulation code. See ADR-0002 for the mapping details.

## D-03 — Source of truth

**Decision (already made by the project owner): the tool is the source of truth. The
spreadsheet becomes export-only.**

This is not a technical decision but it has a technical consequence worth stating: the tool
needs real, durable persistence from Sprint 2 onward (SQLite via `Core/Persistence` ports,
`Infrastructure.Sqlite` implementations) — it is not a viewer or a scratchpad. Every mutation
persists immediately (no "Save" button); the pending-export counter communicates state, it
does not gate it.

## D-04 — `deltaEThreshold`

**Decision: do not resolve the "correct" value now. Only ensure it is calibration data, not a
code literal.**

`deltaEThreshold = 25` (ΔE CIELAB) is marked provisional in the source document — it depends
on the renderer that will eventually draw kits for real, which does not exist yet. Baking it
into code would force a recompile to re-fit it later, and there is no principled value to pick
before a renderer exists to validate against. `WorldCalibration.Constants["deltaEThreshold"]`
carries it as a `CalibrationConstant` (value + unit + note), same as every other number in
`design_handoff_ferramenta_de_mundo/DATA_CONTRACT.md §6`.

## Consequences

- `SoccerDreamGame.sln` grows two new projects (`SoccerSim.WorldBuilder`,
  `SoccerSim.WorldBuilder.Tests`) under a `tools/` solution folder, alongside the existing
  `src/`/`tests/`/`game/` layout.
- `Core/World/` becomes the third major area of `SoccerSim.Core` (alongside the legacy
  `Domain/`/`Simulation/` and the AI/Tactics/Pitch stack), with its own enums, validation and
  economy — deliberately not reusing `Domain.Player`/`Domain.Team`, since the two schemas are
  different shapes, not a renaming exercise.
- No change to `game/` or `Simulation/` in this ADR's scope (Sprints 0–1). The projection that
  bridges the two schemas is deferred to Sprint 6 and does not block the tool's own screens.
