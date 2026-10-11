# ADR-0017 — A club's display code is unique worldwide

- Status: Accepted (already implemented; this records it)
- Date: 2026-10-11
- Supersedes: `WORLD-D20` (unique per country)
- Applies to: `Clubs.DisplayCode` (`UNIQUE`, migration 0007),
  `World/Generation/ClubGenerator.DrawDisplayCode`, `BatchAudit` `CODE_DUP`

## Context

Two legacy documents decided the scope of the three-letter display code, and they disagree:

- **`WORLD-D20`** says it is unique per country, with a deterministic tie-break. Its argument:
  global uniqueness across about 32 leagues forces artificial codes.
- **Club Identity v2 §9.1** says it is unique worldwide. Its argument: continental competitions
  put clubs from different countries on one table and one scoreboard, and per-country uniqueness
  would need a special rule there, which is a second path.

The pilot batch and the code already follow §9.1:
- `Clubs.DisplayCode` is `UNIQUE`;
- the generator checks every existing code;
- the batch audit reports `CODE_DUP` across the whole world.

## Decision

**A display code is unique across the whole world.** `WORLD-D20` is superseded.

The capacity is 17,576 codes (26³). That is enough for several thousand clubs. When a club's
candidates run out, the generator throws, as it already does.

## Options considered

- **Unique per country** (`WORLD-D20`). Rejected. Two clubs with the same code would appear
  together in a continental competition, and telling them apart would need a second rule.

## Consequences

- No code or data changes.
- `docs/legacy/README.md` and `START-HERE.md` drop this item from their open divergences.
