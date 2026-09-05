# ADR-0002: ClubIdentity v2 ↔ legacy schema coexistence and projection plan

- Status: Accepted
- Date: 2026-09-05
- Depends on: ADR-0001 (D-02)

## Context

Two models of the same domain now exist side by side:

| | Legacy (`sql/0001_initial_schema.sql`, `Core/Domain/`) | World v2 (`Core/World/`, this sprint) |
| --- | --- | --- |
| Club | `Teams(Id, Name, LeagueId, Budget, EloRating)` | `ClubIdentity` — 8 nested blocks + `deviationAudit` |
| Player | `Players` — 7 attributes, scale 1–20 | `CharacterRecord` — 12 attributes, scale 1–99 |
| Geography | `Leagues.Country TEXT` | `GeoNode` hierarchy (World → Confederation → ... → City) |
| Competition | `Leagues` + `Seasons`, `Tier ∈ {1,2,3}` | `Competition` — `prestigeBand`, `leagueTierFloat` (continuous), 5-level scope |

`MatchEngine` and `SimulationLODManager` consume only the legacy shape and are already tested
(`MatchEngineTests`, `SimulationLodReservationTests`, etc.). Nothing in this sprint changes
that contract.

## Decision

**Coexist.** `Core/World/` is purely additive:

- `ClubIdentity`, `CharacterRecord`, `GeoNode`, `Competition`, `WorldCalibration` live in the
  `SoccerSim.Core.World` namespace, independent of `SoccerSim.Core.Domain`. No renaming, no
  shared base type — they model different things at different fidelity, not the same thing
  twice.
- The legacy tables/types are untouched and remain the only thing `Simulation/` reads.
- A **derived projection** (`Core/World/Projection/WorldToLegacyProjection`, ROADMAP.md
  Sprint 6 — not built in this sprint) will regenerate `Teams`/`Players`/`Leagues` rows from
  the authored world on demand (`worldbuilder project` command), so a club created in the tool
  becomes playable without either schema being torn down.

### Mapping plan for Sprint 6 (recorded now so Sprint 1's shape doesn't foreclose it)

- `ClubIdentity` → `Team`: `EloRating` derived from `world.clubStrength` (0.3–1.2 observed
  range); `Budget` derived from summed `CharacterRecord.salaryMonthlyBrl` across the club's
  roster.
- `CharacterRecord` → `Player`: the 12 attributes (1–99) map down to the legacy 7 attributes
  (1–20) via an explicit, documented weight table — not a blind linear rescale of a subset.
  The exact per-attribute mapping is Sprint 6 work; this ADR only commits to it being
  **explicit and tested** (`ProjectionTests`), never implicit in a formula that lives only in
  a comment.
- `Competition` → `League` + `Season`: `leagueTierFloat` (continuous) rounds to the legacy
  `Tier ∈ {1,2,3}` via a documented threshold, not a bare cast.
- Projection must be **deterministic and idempotent**: running it twice on the same world
  produces the same legacy rows, and every projected `Team` ends up with ≥ 11 `Player`s (a
  `MatchEngine` precondition).

## Why not migrate `Simulation/` instead

Rewriting `Simulation/` against `ClubIdentity`/`CharacterRecord` directly was considered and
rejected for this phase: it is a larger, riskier change that touches tested, working
match-resolution code for a benefit (avoiding a projection step) that doesn't materialize
until the world-authoring tool and the game's simulation core are unified — which, per
ADR-0001, is explicitly deferred until the game's own core entities are finalized. Coexistence
means the tool ships value (Sprints 3–5: read/edit/generate) without that risk, and the
projection is the seam where the two sides meet on the project owner's own schedule.

## Consequences

- `Core/World/` and `Core/Domain/` will look like two unrelated models for several sprints.
  This is intentional, not a sign the merge was forgotten — ADR-0001 and this ADR are the
  record of *when* and *how* they meet.
- Any code outside `Core/World/` and the future `Core/World/Projection/` must not depend on
  `ClubIdentity`/`CharacterRecord` until the projection exists — the tool's own API/UI
  (Sprints 3+) is the only consumer for now.
- `WorldCalibration` (constants, age curve, prestige bands, position weights) is new
  first-class data with no legacy equivalent; it does not need a projection, only persistence
  (Sprint 2).
