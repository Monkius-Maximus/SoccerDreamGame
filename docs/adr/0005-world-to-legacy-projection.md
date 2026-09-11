# ADR-0005: Projecting the authored world onto the legacy schema

- Status: Accepted
- Date: 2026-09-11
- Depends on: ADR-0002 (coexistence, and the promise that this mapping would be explicit)
- Applies to: `src/SoccerSim.Core/World/Projection/`,
  `src/SoccerSim.Infrastructure.Sqlite/LegacyProjectionWriter.cs`

## Context

ADR-0002 chose coexistence: `Core/World/` models the authored world, `Core/Domain/` stays the
only thing `Simulation/` reads, and a derived projection meets them in the middle. It deferred the
actual mapping to Sprint 6 and committed to one thing about it — that it would be **explicit and
tested**, never implicit in a formula living in a comment. This ADR is that mapping.

## Decision

### 1. The projection is pure; the writer is not

`WorldToLegacyProjection.Project` takes clubs, characters, competitions and geo nodes and returns
a `LegacyWorld` of `League`/`Season`/`Team`/`Player` rows. No database, no clock, no randomness,
so the whole mapping is testable without a file on disk, and the same world always produces the
same rows. `LegacyProjectionWriter` (SQLite, speaking SQL directly) is the only part that touches
storage.

### 2. Ids are positional, and that is why projecting refuses a played database

Legacy keys are integers; world keys are strings (`clb_bra_rio_001`). Ids are assigned by ordinal
position in sorted world-id order. This is deterministic and reproducible on another machine, and
it needs no mapping table — but it is **not stable across a world that gains or loses a club**: a
new club sorting into the middle renumbers every team after it.

A renumbering under a career in progress would leave finished matches pointing at the wrong clubs
— a save that looks fine and is wrong. So the writer counts `Matches` first and refuses if there
are any, naming the count and the remedy. A persistent id-map table was considered and rejected:
it buys id stability for a workflow (re-authoring mid-career) that does not exist yet, at the cost
of a table, a migration and a port. The guard is one query and cannot silently corrupt anything.

### 3. Twelve attributes to seven, with every one of the twelve landing somewhere

`AttributeMapping` holds two weight tables. Weights within each column sum to exactly 1, so a
player at 99 everywhere projects to 20 everywhere and the scale is never quietly inflated.

**Outfield**

| Legacy | From |
| --- | --- |
| Pace | Pace 0.85, Dribbling 0.15 |
| Stamina | Stamina 1.00 |
| Strength | Strength 1.00 |
| Passing | Passing 0.75, Vision 0.25 |
| Shooting | Finishing 0.70, Composure 0.30 |
| Tackling | Tackling 0.70, Positioning 0.30 |
| Vision | Vision 0.60, Positioning 0.25, Composure 0.15 |

**Goalkeeper** — three columns are overridden; everything else comes from the table above.

| Legacy | From |
| --- | --- |
| Passing | Passing 0.80, Composure 0.20 |
| Tackling | Reflexes 0.60, Positioning 0.40 |
| Vision | Handling 0.40, Vision 0.40, Composure 0.20 |

The goalkeeper table is not decoration. Without it, `Reflexes` and `Handling` — the two attributes
that are the entire point of a keeper — reach no legacy column at all, and a world-class keeper
projects to the same seven numbers as a hopeless one. `Shooting` is deliberately **not** overridden
for keepers: a keeper's Finishing is genuinely low, and that low number is what stops the engine's
scoring pool treating him as a threat. Over the real batch, the 73 keepers project to a Shooting of
4–10.

The scale is linear and endpoint-exact: `1 + (v − 1) × 19 / 98`, so 1 → 1 and 99 → 20 and neither
end of the legacy range is unreachable. The legacy columns carry
`CHECK (… BETWEEN 1 AND 20)`; a value outside that is a failed insert, not a rounding quirk, and a
test asserts the whole batch lands inside it.

### 4. Elo, Budget and Tier

- **Elo** = `1500 + (clubStrength − 0.75) × 1000`, clamped to 1000–2200. The midpoint of the
  authored 0.3–1.2 range (ADR-0002) is the engine's own default of 1500, and one full point of
  strength is worth 1000 Elo. Over the current batch (0.66–1.00) that spreads twenty clubs across
  1410–1750 — about the gap a real top division shows between its best and worst side.
- **Budget** = the annual wage bill (`Σ salaryMonthlyBrl × 12`). It is the only figure in the
  world that is genuinely about money the club commits every year.
- **Tier**: `leagueTierFloat ≥ 0.80 → 1`, `≥ 0.55 → 2`, else `3`. The batch's single competition
  carries 0.86, which is its members' mean club strength (measured: 0.8605) — so the float is a
  measure of **quality**, while the legacy `Tier` is a level of **detail**. The mapping states what
  it means by joining them: a stronger competition earns a more expensive simulation. 0.86 lands on
  `ActiveHuman`, the minute-by-minute engine, which is what makes "a club created in the tool plays
  a simulated match" true rather than nominal.

### 5. Only national competitions become leagues

`Leagues` own `Seasons` and fixtures, and a `Team` plays in exactly one. A continental cup is a
competition but not that, so only `Scope = National` projects. A club that belongs to no national
competition, or to two, cannot be represented at all — both are reported by name, all of them at
once, the way the importer reports malformed records.

The season is the calendar year the edition names (1 January – 31 December). Real fixture windows
differ by country and are a scheduling concern; the projection's job is to give the season a span
the fixtures can fall inside, not to invent a calendar.

### 6. Two gaps left open on purpose

- **Traits.** `PlayerTraits` is a legacy catalogue with no authored counterpart. Assigning one
  would be the projection inventing a personality the world never stated, so projected players
  carry none.
- **Seasons had no port.** The dev seed was the only thing that had ever written a season row.
  `ISeasonRepository` fills that gap rather than the writer reaching around the ports for one
  table.

## Consequences

- Projecting is a **pre-career act**. Author, project, then play. Re-authoring mid-career is not
  supported and says so loudly instead of corrupting the save.
- The mapping tables are the contract. Changing a weight changes every projected player, so a
  change needs the same evidence Sprint 1 demanded: run the whole batch through it and look at
  what moved. `ProjectionTests` pins the properties that must survive any such change — endpoint
  exactness, weights summing to 1, every world attribute reaching a column, keepers not projecting
  as finishers, and every team fielding at least eleven.
- `Core/World/Projection/` is the first and only code that depends on both models. ADR-0002's rule
  ("nothing outside `Core/World/` may depend on `ClubIdentity`") now has exactly the one exception
  it always named.
