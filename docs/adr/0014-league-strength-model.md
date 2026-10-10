# ADR-0014 — League strength: one global scale, a measured bridge, a profile per league

- Status: Accepted (not yet implemented — research pack, then Sprint 11e)
- Date: 2026-10-05, amended 2026-10-10 (terminology, ADR-0015)
- Depends on: ADR-0002 (`clubStrength`), ADR-0004 (squad generation), ADR-0011 (generation),
  ADR-0012 (competitions), ADR-0013 (ids), ADR-0015 (terminology)
- Applies to: `src/SoccerSim.Core/World/Generation/` (`DivisionGenerator`, `ClubGenerator`,
  `SquadShape`), `src/SoccerSim.Core/World/Strength/` (new), `data/strength/` (new),
  `tools/SoccerSim.WorldBuilder/`
- Plan: `docs/ROADMAP-GENERATION.md` (strength pack, Sprint 11e)

## Context

A player in the third division of England and a player in Portugal's top flight must not come
out at the same level. Real leagues differ by tens of points on any global rating, and the
generated world has to inherit that difference instead of each division being typed by hand.

Today nothing connects a competition to the strength of the clubs in it:

- `clubStrength` sits on a scale that ADR-0002 calls "0.3–1.2 observed". It is the value the
  prototype happened to produce, not a defined scale.
- The squad's target overall is `XI ≈ 39.6 + 41.35 × clubStrength`, clamped to 52–88
  (`SquadShape.TargetOverallFor`). The fit was made on the twenty Brazilian pilot squads only.
- The club reputations (B1–B6 before ADR-0015) were calibrated with reference leagues: World (B1)
  the five big European leagues, Local (B5) Thailand, Modest (B6) the German 3. Liga. But a
  generated club's reputation is typed by whoever generates it.
- `DivisionGenerator` takes a reputation and a strength range from the author.

The result is visible on the screen. A demonstration Série B was generated at **World** (B1), a
reputation no Brazilian top-flight club holds, and so its squads got 40 players and inflated
market values. Nothing flagged it, because no rule knows what a second division should look like.

`ClubFields` already declares `reputation` and `clubStrength` as **Derived**. For anchored
clubs they derive from the real club; for generated clubs, nothing said from what. This ADR
supplies the rule.

## Decisions

### 1. One global scale: the Opta Power Rankings

Every comparison between leagues and clubs is made on one external scale: the **Opta Power
Rankings**. It rates more than 15,000 teams from 0 to 100, and Opta publishes league averages
from it (June 2025: Premier League 92.6, Primeira Liga 79.8, Brasileirão 79.4). It is
results-based and covers lower divisions, and the project already cites Opta coverage in its
calibration sources.

The scale enters as **evidence**, the way FBref and StatsBomb already do ("no number without a
source"). The pack stores what the model needs, with a citation and a snapshot date:

- each league's quantiles;
- the ratings of the reference clubs the bridge is fitted on (§2).

It never stores a copy of Opta's tables. A pack is authoring data: like the deviation audits, it
is stripped from game builds.

### 2. A measured bridge from the scale to `clubStrength`

`clubStrength` stays the world's own measure, because the projection (Elo, simulation tier) and
the squad generator read it. A **bridge** converts an Opta rating into `clubStrength`.

The bridge is **calculated, never typed**. The pack lists reference points, each a club with its
Opta rating and its authored `clubStrength`. Core fits the bridge from those points by least
squares, and the result is shown, not stored by hand.

- **The twenty pilot clubs are the first points.** They are anchored to real clubs, so each has a
  real Opta rating and an authored strength.
- **The pilot alone is too narrow.** Brazilian top-flight ratings span roughly 76–88. Fitted on
  that span alone and extrapolated, the bridge would put the world's best club above 1.2 and a
  low English division below 0.3. So:
  - **Core never extrapolates.** A league whose quantiles fall outside the span of the reference
    points is refused by name. To profile a league, the pack must first cover its range.
  - **The ends need reference clubs authored the same way as the pilot** (the `CAL-D46` method).
    The Content Workbench's La Liga pilot (Real Madrid, Athletic Club) is the natural first source
    for the top end. The lower end needs a club from a low professional division. This is the
    hardest item of the research pack, and the owner approves the references before they enter.
- **The form of the fit** (linear, or piecewise linear between references) is chosen by the data.
  The pack states which form it uses, and Core supports exactly that form.

### 3. The ends of `clubStrength` and of the target overall are re-derived, not inherited

"0.3–1.2" and the 52–88 clamp were observations. With the bridge, the ends become the bridge's
values at the lowest and highest profiled ratings. `TargetOverallFor` is revalidated at both
ends: an English fourth-division XI and a Premier League XI must land where the overall scale's
own calibration puts them (ceiling 86–88 per `CAL-D46`).

If the Brazil-only fit `39.6 + 41.35 × s` does not hold at the ends, it is re-fitted over the
reference clubs. That is part of the pack too. A clamp that flattens two different leagues to
the same overall (52) is exactly the failure this ADR exists to remove.

### 4. A strength profile per league

The pack has one profile per country and level:

```
{ "countryId": "BRA", "level": 2,
  "provenance": "Measured" | "Estimated",
  "snapshot": "2025-06-11", "n": 20,
  "quantiles": [ 11 Opta ratings at p = 0.0, 0.1, …, 1.0 ],
  "source": { "citation": "…", "url": "…", "accessed": "…" },
  "estimation": null | { "rule": "…", "inputs": ["…"] } }
```

- **Quantiles, not means.** Storing the shape of the distribution captures a league concentrated
  at the top (two giants and a flat field) as well as a balanced one, and it pulls generated
  clubs toward the middle exactly as much as the real league does.
- **"Estimated" is declared, not silent.** A league Opta does not cover is estimated by a rule the
  pack writes down. One example: the gap between levels *n* and *n+1*, measured where both are
  covered. The profile records the rule and its inputs, and the audit shows `STRENGTH_ESTIMATED`
  as a warning. There is no fallback in code: a competition with no profile cannot be generated
  into (`STRENGTH_UNPROFILED`).
- **The snapshot is versioned.** League averages move season to season (the Premier League read
  87.9 in October 2024 and 92.6 in June 2025). A new snapshot is a new pack version. It
  re-derives generated clubs only when the owner recalculates, as the economy already works
  (ADR-0007 §7).

### 5. Generating a division asks only for the competition, the count and the seed

`DivisionGenerationRequest` loses `Band` (now `Reputation`), `StrengthMin` and `StrengthMax`. For
*k* clubs, club *i* (strongest first) takes the quantile at `p = 1 − (i − 0.5) / k`. Core
interpolates it linearly between the profile's points, converts it through the bridge, and
rounds to two decimals as the pilot writes strengths.

The ladder is exact, with no jitter: a seeded spread would be a number with no source. Different
seeds still give different leagues (names, cities, squads), and each squad keeps its Gaussian
spread around the target overall.

### 6. The reputation of a generated club starts from its strength

*Amended 2026-10-10 (ADR-0015 §4).* Two ideas used to share the word "band":
- **Strength** is a continuous value. Its derived **rating class** is shown to the player as
  stars and is never stored.
- **Reputation** is a club's standing and size. It is a separate axis that market value, stadium
  capacity and squad size read.

Leagues have no reputation and no band. They have a `CompetitionStrength`, the mean strength of
their season's participants.

The pack declares reputation thresholds on the global scale. Each threshold is derived from the
reference leagues the calibration already uses for World…Modest, and the derivation is written
down.

- A **Generated** club's reputation is the reputation its Opta-equivalent rating falls in. A new
  club has no history, so its standing starts equal to its strength. It is a Derived field:
  `WorldDerivations.Recalculate` sets it, and the screen does not offer it for editing.
- An **Anchored** club keeps its authored reputation, with a source (`CAL-D38`, `CAL-D51`). Its
  reputation may differ from its current strength, as with a historic club in a lower division,
  and that is the point of authoring it.

The squad size follows the reputation (`squadSizeByReputation`), so it follows automatically.

### 7. A single generated club keeps the author's strength, and the audit compares it

`generate-club` creates one club the author designs, and its strength stays an input. Its
reputation is derived (§6). When the club is enrolled in a competition, the audit warns
`STRENGTH_OUTSIDE_LEAGUE` if its strength falls outside that league's profiled range. The club is
allowed, but it is visible.

### 8. The national team validates; it does not drive

National-team strength is **not** an input. Opta's league ratings already come from the clubs'
real results, continental competitions included. Adding the national team as a weight would
count the same evidence twice.

It becomes a **validation** once national teams exist (the `wrldi` competitions): the best
generated players of a nationality must form a side whose strength is coherent with that
country's real standing. A large gap is an audit finding about the profiles, not a knob to turn.

### 9. Validation the implementation must pass

- **Pilot reproduction.** Brazil level 1's profile, put through the fitted bridge, reproduces
  the pilot's authored strengths within a tolerance the pack states. The test prints the
  residuals.
- **Order across leagues.** For every pair of profiled leagues, if one league's median Opta rating
  is higher, its generated median strength and median XI overall are higher too.
- **No extrapolation.** A profile outside the bridge's span is refused.

### 10. Where packs live

Importable documents (club profiles, squad profiles, strength profiles, and the name pools to
come) live in `data/<pack>/`, versioned, each with its sources. `tests/.../TestData` keeps
fixtures, which are copies pinned for tests. Production data stops living inside test
fixtures. `docs/DATA-PACKS.md` is the contract every pack follows.

## Options considered

- **Keep typed reputations and strength ranges, add warnings.** Rejected: the author still
  invents the number, and the warning would need the profile anyway.
- **Store clubs on the Opta scale directly.** Rejected: the projection and the generator read
  `clubStrength`, and replacing the world's measure is a larger change than adding a bridge.
- **An Elo source (ClubElo) instead of Opta.** Rejected for now: ClubElo covers Europe only, and
  the world starts in Brazil.
- **A composite index** (league prestige, real-club strength and national team, weighted).
  Rejected: the weights would have no source, and Opta already folds the club evidence in. The
  national team stays as validation (§8).
- **Reputation as a class of leagues** (the July *Sistema de Prestígio* research). Rejected on
  2026-10-10: the code reads reputation per club, and a historic club in a lower division needs
  its own. The research's league classes remain the reference sets that calibrate the reputation
  thresholds and the per-reputation tables.

## Consequences

- The "Gerar divisão" dialog asks for the count and the seed only. It shows:
  - the profile used (snapshot, provenance);
  - the strengths it will produce;
  - the reputations they derive.
- The demonstration Série B and Série C in the test databases are regenerated in Sprint 11e.
  Their World reputation and 40-player squads were inputs, not data.
- Generating a division needs a profile for its country and level, so the research pack comes
  before Sprint 11e.
- ADR-0002's "0.3–1.2" becomes "the bridge's range". ADR-0004's target-overall fit is
  revalidated, and re-fitted if it fails at the ends.
