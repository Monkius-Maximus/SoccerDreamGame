# ADR-0018 — Stadium capacity of a generated club: the country's profile, shifted against the country's clubs

- Status: Accepted (not yet implemented — Sprint 11f)
- Date: 2026-10-11
- Amends: `CAL-D39` (the reference of the shift)
- Depends on: ADR-0015 §4 (club reputation; leagues have none)
- Applies to: `World/Generation/ClubGenerator.Capacity`, `DivisionGenerator`, `WorldCalibration`

## Context

`CAL-D39` decided how a club's stadium capacity is drawn:
- it starts from a truncated Gaussian over the **country's stadium profile** (mean, deviation,
  minimum and maximum, taken from real stadiums);
- it is shifted by **0.45 deviation for each reputation step the club sits below its league's
  band**.

The reason was measured: capacity follows the country's urban history, not the club's sporting
level. A per-band table produced a 93,000-seat stadium for a mid-table club.

Two things broke that rule:

- **ADR-0015 left leagues without a reputation.** The shift has no reference any more.
- **The code never followed `CAL-D39`.** `ClubGenerator.Capacity` draws from the band's mean and
  deviation (`CapMean`, `CapSd`), and then clamps the result to the country's minimum and
  maximum. Clamping piles clubs on the bounds, and it is the silent coercion the project rules
  out.

## Decision

### 1. The reference is the median reputation of the country's clubs

The shift is measured against the **median reputation of the clubs of the same country**. The
median is calculated at generation time, over:
- the clubs already in the world;
- the clubs of the same request, after their reputations are set and before any capacity is
  drawn.

It is derived and never stored.

```
rank:   World = 1, Continental = 2, National = 3, Regional = 4, Local = 5, Modest = 6
ref   = median of the ranks of the country's clubs (may be a half-step, e.g. 2.5)
shift = (ref − rank(club)) × 0.45                                   (in deviations)
μ'    = profile.Mean + shift × profile.Sd
```

Including the request's own clubs keeps one rule for every case:
- the first division of a country with no clubs is its own reference, so the shift is 0;
- a lone club in an empty country is its own reference too.

### 2. A real truncated draw, never a clamp

Values are drawn from `N(μ', profile.Sd)` and kept only when they fall inside
`[profile.Min, profile.Max]`, after both bounds are aligned to the capacity step. The result is
then rounded to the step.

- A draw outside the bounds is drawn again, on the same seeded stream.
- After 1,000 rejected draws, the generator throws and names the country and the reputation.
  That means the profile cannot hold this club, and it is a data error.

### 3. The per-reputation capacity columns leave the generator

`CapMean`, `CapSd` and `N` in the reputation calibration (the band table built from 270 real
stadiums) stop driving generation.
- They remain data, used to build a country's profile when no direct measurement exists, as
  `CAL-D39` intended.
- The value multiplier stays where it is.

### 4. Capacity is drawn once

Capacity is a `Sampled` field. A club keeps its capacity when the median moves later, as more
clubs enter the world. The median is read only while a club is being generated.

## Options considered

- **Each reputation maps to a fixed quantile of the country's profile.** Rejected by the owner in
  favour of a reference relative to the country's clubs, which stays closer to `CAL-D39`.
- **Keep the per-band draw with a clamp.** Rejected. It contradicts `CAL-D39`, and the clamp
  piles clubs on the bounds.
- **Use the reputation of the competition the club enters.** Rejected. Competitions have no
  reputation (ADR-0015).

## Consequences

- Generated capacities change. Sprint 11f re-pins the generation tests once, together with the
  determinism fixes of ADR-0019, which also move every Gaussian draw.
- The pilot's anchored clubs keep their authored capacities.
- The relative reference means that the same club generated into a different world may get a
  different stadium. That is accepted: generation is defined over a world, not in a vacuum.
- `START-HERE.md` lists this divergence as scheduled for Sprint 11f.
