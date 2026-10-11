# ADR-0019 — Determinism in the core: one Gaussian, no trigonometry in the pitch, no random ids

- Status: Accepted (not yet implemented — Sprint 11f)
- Date: 2026-10-11
- Applies to:
  - `src/SoccerSim.Core/Random/IDeterministicRandom.cs`;
  - `src/SoccerSim.Core/Pitch/Vec2.cs` and `PitchSimulation.cs`;
  - `src/SoccerSim.Core/Events/EventManager.cs`.
- Follows: `SIM-D34` (Irwin-Hall), `PHYS-D42` (no transcendental functions on the hot path), the
  rule in `CLAUDE.md` ("the same seed must reproduce the same output")

## Context

The repository requires bit-for-bit replay: the same seed gives the same world and the same
match, on any machine. Three places in the core break that, or put it at risk.

1. **`IDeterministicRandom.NextGaussian` uses Box-Muller** (`Math.Log`, `Math.Cos`, `Math.Sqrt`).
   The precision of `Log` and `Cos` depends on the platform's maths library. The design decided
   on an Irwin-Hall approximation for this reason (`SIM-D34`), but the core never adopted it.
   Every generated squad and stadium uses this draw.
2. **`Vec2.Rotated` uses `Math.Cos` and `Math.Sin`.** `PitchSimulation` calls it on every pass and
   shot, to apply the angle error. The angles are small: below 0.12 rad.
3. **`EventManager` gives each life event a `Guid.NewGuid()` id.** The same seed produces the
   same events with different ids.

`TimeManager` also uses `Guid.NewGuid()`, for its calendar resume token. That token is a session
handle: it is never stored and never changes an outcome. It is not a determinism problem.

## Decision

### 1. One Gaussian: Irwin-Hall

`NextGaussian` returns the sum of twelve `NextDouble()` draws minus 6. The result has mean 0 and
deviation 1, and it is bounded to ±6. No transcendental function is involved.

There is no second Gaussian. Box-Muller is removed, not kept beside it.

### 2. Rotation by small angles uses polynomials

The pitch's rotation is computed with `sin θ ≈ θ − θ³/6 + θ⁵/120` and
`cos θ ≈ 1 − θ²/2 + θ⁴/24`, then renormalised so that the vector's length is kept exactly. At
0.2 rad the error is below 10⁻⁷. `PHYS-D42`'s shorter form, without the last terms, already
errs by about 7·10⁻⁵ in the cosine at 0.2 rad, so the extra terms are kept.
- The rotation is defined only for `|θ| ≤ 0.2` rad.
- A larger angle throws: it would mean a caller is using it for something it was not built for.

### 3. Event ids are derived, not random

A life event's id is derived with `StableHash` from the player id, the event definition key and
the date. `RollForDay` returns at most one event per player per day, so the triple is unique. If
two events with the same triple ever appear, it throws.

### 4. Out of scope, recorded

- **The legacy Elo resolvers use `Math.Pow`.** They are due to be replaced by the double Poisson
  resolution, and they are not changed here.
- **The World Builder's colour and economy formulas use `Math.Pow` too.** They are authoring
  calculations checked against golden values, not replayed simulation.

## Options considered

- **Keep Box-Muller.** Rejected. It contradicts `SIM-D34`, and its accuracy depends on the
  platform.
- **A lookup table for sine and cosine.** Rejected. It is more code for angles that the
  polynomials already cover with an error below 10⁻⁷.
- **Event ids from a counter.** Rejected. A counter depends on the order of the calls, and the
  triple does not.

## Consequences

- Every generated squad and capacity changes once.
- Sprint 11f re-pins the generation and pitch tests in the same commit as ADR-0018.
- The pilot fixture (`world.json`) does not change. Its players were authored by the pipeline,
  not drawn by this core.
- `START-HERE.md` moves these three divergences to "scheduled for Sprint 11f".
