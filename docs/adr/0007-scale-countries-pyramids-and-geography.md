# ADR-0007: Scale — countries, pyramids, geography edits and the calibration review

- Status: Accepted
- Date: 2026-09-11
- Depends on: ADR-0003 (what the schema constrains), ADR-0005 (the legacy projection)
- Applies to: `src/SoccerSim.Core/World/Competitions/`, `src/SoccerSim.Core/World/GeoTree.cs`,
  `src/SoccerSim.Core/World/Validation/CalibrationReview.cs`, `sql/0014_world_countries.sql`

## Context

`ROADMAP.md` Sprint 9 frames itself precisely: "o que ficou de fora e passa a doer quando existir
a segunda liga" — what was left out and starts hurting the moment a second league exists. Every
decision here is about a world that has more than one country in it, which the pilot does not.

## Decision

### 1. Divisions are not Competitions

A `Competition` row is the **frozen edition** of a competition: its member list, its season, its
prestige band, its 38 rounds of 2026. A `Division` is the **standing structure** editions hang
off: it outlives a season, and it is what promotion and relegation connect.

Collapsing the two makes "who came up last year" unanswerable, and it would have forced `Tier` and
`CompetitionFormat` onto a record the pilot's world document has no fields for — breaking every
import of the real batch to model something the batch does not contain. Divisions therefore live
in their own table (migration 0014), purely additively, and the existing competition keeps
working untouched.

### 2. Rounds and matches are derived, never typed

`CompetitionFormats.Shape(format, clubs)` computes both from the five formats. A typed round count
is a number with no source, and the project runs on the opposite rule.

| Format | Rounds | Matches |
| --- | --- | --- |
| LeagueSingle | n−1 (even), n (odd) | n(n−1)/2 |
| LeagueDouble | 2(n−1) (even), 2n (odd) | n(n−1) |
| GroupsKnockout | 3 + bracket over 2 per group | groups × 6 + qualifiers − 1 |
| KnockoutOnly | ⌈log₂ n⌉ | n−1 |
| NationalCup | 2⌈log₂ n⌉ − 1 | 2(n−1) − 1 |

The odd-field case is the one that goes wrong quietly: a round robin over an odd field takes **n**
rounds, not n−1, because one club sits out each round. A fixture list one round short is not
visible until the last weekend of a season.

The pilot league proves the whole table: 20 clubs playing `LeagueDouble` derive to 38 rounds and
380 matches, which is what the authored data says and what the real Brasileirão plays.
`NationalCup` is distinguished from `KnockoutOnly` by being two-legged except the final — a real
distinction rather than two names for one bracket.

### 3. The flow balance is the pyramid's load-bearing rule

For a division, clubs arriving are those relegated out of the division above plus those promoted
in from below; clubs leaving are those promoted into the division above plus those relegated out
of this one. When the two differ, the division changes size every season — silently, and only
visibly three seasons later.

An imbalance at the top necessarily unbalances the division beneath it. That is correct, not
noise: the flow is a chain, and reporting only the first link would leave the user fixing one
number at a time.

### 4. Country profiles exist because a country without one cannot be populated

`CountryProfile` carries the currency, the exchange rate, the wage floor and the **nationality
mix**. The mix is required, not optional: the generator now takes a profile and refuses without
one, because inventing where a country's players come from quietly makes everyone Brazilian. The
batch sweep reports a missing profile and a missing mix as errors.

**`CountryId` is the ISO code ("BRA"), not the geo node id ("geo_bra").** The batch already uses
the code in `geography.countryId` and keys its stadium profiles by it. The two are different
identifiers for the same place and nothing links them directly, so the country's *name* is reached
by walking up from a club's geo node to its Country-kind ancestor. There is no foreign key because
there is no table of ISO codes to point at. (This was found by importing: an FK to `GeoNodes`
failed on every row of the real batch.)

Brazil's profile is **seeded on import**, measured from the batch itself, with
`NationalityMixSource` left null. A distribution measured from the data it will go on to generate
is not evidence about the world, and the sweep says so — `NATIONALITY_UNSOURCED` is a warning on
the pilot batch today, by design.

### 5. Per-country name pools are deliberately not modelled

The roadmap lists a name pool among what creating a country needs. It is not built. The generator
has one measured pool, and the data for a second country's does not exist for any country but
Brazil. Building a schema for data nobody has is speculation; the honest position is that a second
country will need its pools re-measured exactly as Brazil's were, and that the gap is recorded
here rather than papered over with a nullable column nothing reads.

### 6. Geography edits are pure functions over the whole tree

`GeoTree` takes the nodes and returns the nodes. Each rule exists because breaking it produces a
world that looks fine in a list and is unreadable as a hierarchy:

- **Moving into your own subtree** makes a node its own ancestor, and every walk upward — the club
  page's breadcrumb, for one — stops terminating. The move is refused, and the screen's parent
  select omits the subtree so the impossible move is not offered either. Both, because a select is
  a convenience and a rule is a rule.
- **Deleting a node with children** orphans a branch; **deleting one with clubs** turns into one
  `GEO_DANGLING` finding per club, for a mistake that took one click. Both are refused with the
  reason and the count, and the tree screen shows that reason before the button is pressed.
- **A child's kind is not chosen**: it is the next one in the hierarchy. Offering a choice would be
  offering a way to break it.

### 7. The calibration's source seal is read off its own note

The authored calibration already writes "NÃO ANCORADO" and "PROVISÓRIO" in plain sight.
`CalibrationReviewer` reads those rather than keeping a second flag beside them — a flag that can
disagree with the note is worse than no flag. Over the real calibration that is three unsourced
constants and one provisional, and the screen colours them.

Recalculating the batch is one act because a re-fit constant ages all 688 players at once, and
correcting them one edit at a time is not something a person does. It is idempotent, and
`ECONOMY_STALE` is empty in the sweep afterwards.

## Consequences

- `SquadGenerator.Generate` gained a required `CountryProfile`. Every call site passes one; there
  is no overload that defaults it, because the default is the bug.
- The audit's golden differs by surface: `BatchAudit.Run(world)` alone still reports 1 error and 4
  warnings, while the HTTP audit — which also sees the country profiles — reports 1 error and 5,
  the extra one being Brazil's unsourced mix. Both are pinned by tests.
- The tool now has four screens and a real tab router. The rail stays on all of them: it is the
  batch, and every screen is about the same clubs.
- What Sprint 9 lists and this does not yet build: the undo stack, deleting a club or a player,
  the probable XI on the club page, global search, and the multi-league authoring UI (the pyramid
  is modelled, validated, persisted and displayed, but divisions are not yet created from the
  screen). These are additive to what is here and none of them changes a decision above.
