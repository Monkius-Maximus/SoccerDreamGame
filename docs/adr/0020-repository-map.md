# ADR-0020 — Repository map: this repository is canonical; sibling games consume its world

- Status: Accepted
- Date: 2026-10-15
- Depends on: ADR-0001 (the World Builder is the source of truth for world data), ADR-0012 §1
  (the tool authors the structure; the game plays it), ADR-0015 (one term per concept)
- Applies to: `START-HERE.md`, `docs/TERMS.csv`, and every reference to another repository

## Context

The owner works on football games in more than one repository, and a newcomer cannot tell which
one holds the truth.

| Repository | What it holds |
| --- | --- |
| `Monkius-Maximus/SoccerDreamGame` (this one) | The 2D game, the World Builder and its world data, the design (`docs/legacy/`), the term base, ADR-0001 to ADR-0020. The World Builder work since September 2026 is here. |
| `Monkius-Maximus/new-soccer-world` | Another football game: Godot 4.7.1, .NET 10, a deterministic match core. Last commit on 2026-09-03. |
| `Monkius-Maximus/testingclaude` | "FootballGame": a 3D football game in Godot 4.3. A skeleton with no scenes, from June 2026. |
| `Monkius-Maximus/SDG-AUTODOING` | Work packages for autonomous data agents. The first one asks for the strength pack and the name pools that the World Builder imports. Not a game. |

Three things collide:

- **The canonical claim.** new-soccer-world's ADR-0006 says: "`Monkius-Maximus/new-soccer-world`
  is the canonical runtime repository. `Monkius-Maximus/SoccerDreamGame` is legacy reference
  material." Its `docs/LEGACY_MIGRATION_MATRIX.md` covers only match simulation code. It does not
  mention the World Builder, club identity, calibration or generation.
- **ADR numbers.** Both repositories have an ADR-0001 to ADR-0006, with different decisions.
- **"WorldBuilder".** new-soccer-world also has a `tools/SoccerSim.WorldBuilder`. It builds a
  `world_template.db` from SQL seeds, and it is a different program from this repository's World
  Builder.

## Decision

### 1. This repository is canonical

SoccerDreamGame is the canonical repository of the project. It owns:
- the world: the World Builder, the world data and the calibration;
- the design and the decisions;
- the term base.

new-soccer-world's ADR-0006 is superseded on this point. Its own text still says the opposite
until it is amended in that repository.

### 2. The sibling games consume the world; they do not author it

new-soccer-world and testingclaude are **sibling games**: other football games, in 2D or 3D, built
over the same world. What they reuse is the part of the world that has no spatial treatment:
- clubs, players and staff;
- countries and cities;
- competitions.

- **One author.** The World Builder authors the world for every game, as it does for this one
  (ADR-0001, ADR-0012 §1). A sibling game does not keep a second copy of world data that it edits
  by hand.
- **No audit data.** The IP rule applies to every game build: audit data never reaches a sibling
  game either (`CLAUDE.md`, IP hygiene).
- **Contract still open.** How a sibling game reads the world (the export, a database, a shared
  library) is not decided here. It is decided when a sibling game first needs it, in a new ADR.

### 3. Agent work packages feed the World Builder

SDG-AUTODOING's packages produce data files for the World Builder.
- Their output enters the world only through the World Builder's strict importers.
- Their vocabulary follows `docs/TERMS.csv`.

### 4. Citing another repository's decisions

An ADR number belongs to its repository. A bare "ADR-0006" in this repository means this
repository's ADR-0006. Another repository's decision is cited with the repository's name, for
example "new-soccer-world ADR-0006".

### 5. "World Builder" means this repository's tool

The name refers to `tools/SoccerSim.WorldBuilder` in this repository. new-soccer-world's program
of the same path is called "new-soccer-world's template builder" when it is mentioned here.

## Options considered

- **new-soccer-world is canonical, and this repository becomes legacy** (its ADR-0006). Rejected
  by the owner. The world authoring work, the design and the decisions all live here, and that
  repository's migration matrix does not cover them.
- **Merge the games into one repository.** Not chosen. The owner wants to try alternative games,
  in 2D and in 3D, over one world.

## Consequences

- `START-HERE.md` describes the sibling repositories and what each one is for.
- `docs/TERMS.csv` gets a row for each repository.
- new-soccer-world's README and ADR-0006 contradict this decision until that repository is
  amended. That is the owner's change to make there.
- Le Grand Strategos stays as `START-HERE.md` describes it: a separate game that shares the idea
  of a world builder, but no code.
