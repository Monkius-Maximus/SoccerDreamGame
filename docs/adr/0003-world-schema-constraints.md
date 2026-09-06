# ADR-0003: What the world schema constrains, and what it deliberately does not

- Status: Accepted
- Date: 2026-09-06
- Depends on: ADR-0002 (schema coexistence)
- Applies to: `sql/0006_world_geo.sql` … `sql/0011_world_competitions.sql`

## Context

`design_handoff_ferramenta_de_mundo/DATA_CONTRACT.md §7` sketches DDL for the world-authoring
tables and says the constraints are "as invariantes viradas em SQL" — the invariants turned into
SQL. Implementing that literally against the real batch does not work, and the reason is worth
recording, because the missing constraint looks like an oversight.

## Decision

**A constraint enforces structure. An invariant enforces authoring quality. Only the first
belongs in the schema.**

- **Structural corruption** — a duplicate `DisplayCode`, an attribute of 150, a shirt number
  twice in one squad, an enum value outside the closed set, a club pointing at a geo node that
  does not exist — is rejected by the database. These states have no valid meaning; storing one
  corrupts every consumer downstream.
- **Authoring invariants** — the six club checks in `ALGORITHMS.md §5` — are NOT database
  constraints. They are computed by `Core/World/Validation/ClubInvariants` and surfaced as
  findings. Two reasons, both load-bearing:
  1. The tool must let a user save a work-in-progress club that currently violates a rule. The
     handoff README is explicit ("Portão de invariantes"): errors are shown on the club page but
     never block editing. Only the new-club wizard gates on them.
  2. The batch audit (Sprint 8) can only report a bad row that is *in the database*. A constraint
     that rejects the row makes the problem invisible in the one tool built to find it.

### The three deviations from DATA_CONTRACT.md §7

1. **No `CHECK (PhoneticSimilarity BETWEEN 0.55 AND 0.80)`** on `ClubDeviationAudit` or
   `CharacterDeviationAudit`. The real batch contains `clb_bra_bel_001` (Clube Náutico Remolar,
   `NamingRule = Phonetic`, similarity `0.867`) — a genuine, known violation. With the CHECK
   applied, that club cannot be imported at all, so the tool can never show it and the user can
   never fix it. It is the "1 erro" the roadmap's own Sprint 8 golden expects the audit sweep to
   report. The window is enforced as invariant check #2 instead.
   Covered by `WorldConstraintTests.PhoneticSimilarityOutsideTheWindow_IsStored_AndReportedAsAnInvariantError`
   and `...TheRealBatchesKnownViolation_SurvivesImportAndIsReported`.

2. **`WorldSources.Numero`, `.Fonte` and `.Url` are nullable**, where the sketch has them
   `NOT NULL`. One of the 27 real rows is a prose cross-reference with all three null and the
   whole text in `Tema` (DATA_CONTRACT.md §5 describes it). It is a note, not a broken link, and
   the UI must not render an empty `<a href>` for it.

3. **`CharacterDeviationAudit.DeviationMethod` is nullable.** All 400 `Regen` players in the
   batch carry null there, which is correct: a generated player has no anchor, so there is no
   deviation to describe. (`ALGORITHMS.md §6.10` suggests a sentinel string; the exported data
   uses null, and the data is what has to load.)

### Two constraints that also cannot be row-level

- **Position weights summing to 1 per position** cannot be a `CHECK`: a row-level constraint
  cannot see the other 11 rows, and a half-edited matrix has to be storable while the user edits
  it. It is checked on the calibration screen.
- **Derived values** (`DeltaE`, `HomeLuminance`, `PolarityRule`, `HomeAdvantageModifier`,
  `Overall`, `PotentialOverall`, `MarketValueEur`, `SalaryMonthlyBrl`, `ShirtName`) are stored
  for query convenience but never trusted from a caller. `WorldDerivations` recomputes them
  inside the repository write path, so persisting a stale one through the port is not possible.
  Recalculating on import is only safe because the Sprint 1 gate proved the formulas reproduce
  every recorded value in the batch exactly.

## Consequences

- The schema will look "under-constrained" next to DATA_CONTRACT.md §7. It is not: the missing
  constraints moved to `ClubInvariants`, which reports them without blocking. Do not add them
  back without re-reading this ADR — the phonetic CHECK in particular will make the real batch
  unimportable, and the failure will look like a corrupt data file rather than a schema decision.
- `Age` and `Phase` are stored, not derived, even though the data contract marks them derived.
  297 of the 688 players disagree with a birthday-aware age at the contract's reference date, and
  age feeds the market-value curve — deriving it would silently rewrite their economy on first
  save. Widening the derived set needs the same evidence Sprint 1 produced: run the whole batch
  through it and check that nothing moves.
