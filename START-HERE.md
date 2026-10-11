# Start here

A guide for anyone new to this repository, whether developer, designer or AI agent. It says
what the project is, where each kind of truth lives, what to read first, and where the design
and the code still disagree. The current status of the code was checked at commit `7ec9d3e`.

## What this is

**Soccer Dream Game** is a 2D football life and career simulator in Godot 4.6, C# and .NET 8,
built by one person (Diogo). Its world is called **Terra**:
- the nations and cities are real;
- every club and player is fictional;
- many are parodies derived from real ones, with no real name reaching the game.

The repository holds two things:

1. **The game:** an engine-agnostic core (`src/SoccerSim.Core/`) and a thin Godot shell
   (`game/`). See `README.md` and `docs/ARCHITECTURE.md`.
2. **The World Builder** (`tools/SoccerSim.WorldBuilder/`): the authoring tool that is the
   source of truth for the world's clubs, squads, geography and competitions. Most recent work
   is here.

**Sibling projects.** Le Grand Strategos (LGE) is a separate grand-strategy game in Unreal
Engine 5. It shares the *idea* of a world builder used to set up game modes, but no code. Two
other football projects exist; their relationship to this one is still to be mapped.

## Where each kind of truth lives

| You want… | Look in | Language |
| --- | --- | --- |
| How things work now | The code | English |
| A decision made since August 2026 | `docs/adr/` (ADR-0001 to ADR-0016) | English |
| The name of a concept, and what the player sees for it | `docs/TERMS.csv` | English, with pt-BR and en-US labels |
| A decision made before the ADRs (`GEO-D53`, `CAL-D38`…) | `docs/decisions/LEGACY-IDS.md` → `docs/legacy/` | pt-BR, frozen |
| What is being built next | `docs/ROADMAP-GENERATION.md` | English |
| The rules for working in the repository | `CLAUDE.md` | English |

**When sources disagree:**
- an ADR beats a legacy document;
- the code beats a plan;
- a disagreement is recorded, never silently resolved (see "Known divergences" below).

## Reading order

1. `README.md` and `docs/ARCHITECTURE.md`: the layers and the golden rule (simulation outside
   Godot).
2. `CLAUDE.md`: hard rules (one path, fail fast, derived state is never stored, determinism).
3. `docs/TERMS.csv`: one term per concept. In particular, "tier" means only the simulation's
   level of detail, and "band" no longer exists. It became **rating class** and **club
   reputation** (ADR-0015).
4. `docs/ROADMAP-GENERATION.md`: the current sprint, then the ADRs it cites.
5. `docs/legacy/README.md`: only when an ADR or a document cites a legacy decision.

## What exists, and what is design only

| Area | State |
| --- | --- |
| World Builder: clubs, squads, geography, competitions, generation, export | **Code**, Sprints 0–11c. Next: 11d (ids and names), then 11e (league strength) |
| Background simulation: calendar, LOD tiers, Elo resolvers, SQLite saves | **Code**, the original core (legacy schema, ADR-0002) |
| 2D pitch: steering and utility AI | **First code** in `src/SoccerSim.Core/Ai/` and `Pitch/` |
| Godot scenes: menu, calendar, match, life sim | **Shells** with thin autoloads |
| Tier 1.5 structured resolver, referee, environmental physics, plot editor, character creation and lifecycle, career life | **Design only**, in `docs/legacy/`. Most wait behind the implementation sequence `CORE-D07` |

## Known divergences between design and code

Each of these is either scheduled or waits for a decision. None should be "fixed" in passing.

| What | Design says | Code does | Where it is handled |
| --- | --- | --- | --- |
| Football confederation | A field of the country, not a geographic level (`GEO-D53`) | `GeoNodeKind.Confederation` is a tree level | Sprint 11d |
| Stadium capacity | Country stadium profile, shifted by reputation (`CAL-D39`) | Drawn from the band's mean and spread | Open, `docs/RENAMES-0020.md` §E |
| `displayCode` uniqueness | Per country (`WORLD-D20`) vs. global (club identity v2 §9.1) | Global (`UNIQUE`) | Open, needs a decision |
| LOD tier assignment | Dynamic, per match, by competitive distance (`GEO-D32`/`GEO-D33`) | Static per league (`SimulationTier`) | Not scheduled |
| Background match resolution | Double Poisson | Elo → goals (`EloResolverBase`) | Not scheduled |
| Gaussian draws | Irwin-Hall, no transcendental functions (`SIM-D34`) | Box-Muller with `Math.Log`/`Math.Cos` (`IDeterministicRandom.NextGaussian`) | Not scheduled |
| Ball deflection | No `Math.Sin`/`Math.Cos` in the hot path (`PHYS-D42`) | `Vec2.Rotated` uses them, called from `PitchSimulation` | Not scheduled |
| Deterministic ids | No `Guid.NewGuid()` in simulation | Used in `EventManager` and `TimeManager` | Not scheduled |

## Things that are not in this repository yet

- **The World Builder handoff** (`design_handoff_ferramenta_de_mundo/`, the `WBH` decisions).
- **The reconstructed ledger and the art-direction decisions** (patch F0-A: `CORE`, `RND`,
  `LED`).
- **Staging code and authoring scripts** that live in the owner's Claude project:
  - `SimCore*.cs`;
  - `tp_*.py`.

Ask the owner before depending on any of them.
