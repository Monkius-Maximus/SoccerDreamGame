# ADR-0013 — One identifier convention: entity, locator, origin

- Status: Accepted (not yet implemented — Sprint 11d)
- Date: 2026-10-05, amended 2026-10-10 (ADR-0015: geo nodes above the country are continents)
- Depends on: ADR-0007 (country codes vs geo nodes; §4 amended here), ADR-0008 (levels renumber), ADR-0012
  (competitions and seasons), ADR-0015 (terminology)
- Applies to: every id the world stores; `src/SoccerSim.Core/World/Ids.cs` (new),
  `sql/0020_identifier_convention.sql`, the fixtures under `tests/SoccerSim.Core.Tests/TestData/`
- Plan: `docs/ROADMAP-GENERATION.md`, Sprint 11d

## Context

The world mixes id shapes, and each new entity has invented its own:

| Entity | Ids today | Problem |
| --- | --- | --- |
| Club | `clb_bra_rio_001` | — |
| Geo node | `geo_world`, `geo_conmebol`, `geo_bra`, `geo_bra_sudeste`, `geo_city_rio` | A city carries a kind (`city`) where the others carry a country |
| Player (pilot) | `plr_rio_001_0001` | No country |
| Player (generated) | `plr_gen_brasaogoncalo001_0001` | A third shape: `gen` where the country goes, underscores stripped |
| Competition | `cmp_bra_tier1`, `bra_t2`, and the screen suggests `div_bra_4` | Three shapes, and a level that changes when a division is removed (ADR-0008 §2) |
| Season | `edt_bra_tier1_2026` | The prefix says "edition", and the entity is now `CompetitionSeason` |

Nothing has been released and no career save exists. Renaming now costs one migration. Renaming
after the world is populated, or after a save holds these ids, costs far more.

## Decisions

### 1. The shape: `<entity>_<locator>_<rest>`

- **Entity**: a fixed three-letter prefix that says what the string is. Nothing else may use that
  prefix.
- **Locator**: where the entity belongs.
  - The country's FIFA trigram in lower case (`bra`, `eng`, `uru`) for anything that belongs to a
    country.
  - `wrld` for geo nodes above country level (the world, continents).
  - For competitions without a country: `wrldi` for national-team competitions, `wrldt` for
    club competitions (continental and intercontinental), and `wrldc` for custom competitions an
    author creates with other parameters.
- **Rest**: lower-case ASCII letters and digits, with segments separated by `_`.

`CountryId` is the upper-case FIFA trigram (`BRA`), and the locator is its lower-case form.

**Why FIFA and not ISO 3166.** In football the country is the football association, not the
state. England, Scotland, Wales and Northern Ireland each run their own pyramid, and ISO has only
`GBR` for all four. The pilot already writes nationalities as FIFA trigrams (`URU`, `PAR`, `CHI`,
`NED`, `DEN`, `ANG`, `POR`; ISO would be `URY`, `PRY`, `CHL`, `NLD`, `DNK`, `AGO`, `PRT`). So the
world's countries and its nationalities use one code system. Brazil is `BRA` in both systems, so
no stored row changes. This amends ADR-0007 §4, which called `CountryId` an ISO code.

### 2. An id names the origin, never the current state

An id must not contain anything that can change. A competition's level renumbers when a level
above it is removed, its name will be revised, a club can move and a player can be transferred.
So:

- **Competitions get a sequence**, `cmp_bra_001`, allocated as the next free number for its
  locator. The level and the name live in fields.
- **A player id names the club that created it** (`plr_bra_rio_001_0001`). It records where the
  player was created, not where he plays. After a transfer, the id is still true.
- **A geo node carries no kind.** The kind is already a field of the node, and repeating it in the
  id would store one fact twice. A region and a city of the same country cannot share a slug: the
  primary key refuses it, and the author picks another.

### 3. The table

| Entity | Prefix | Shape | Example |
| --- | --- | --- | --- |
| Geo node, world | `geo` | `geo_wrld` | `geo_wrld` |
| Geo node, continent | `geo` | `geo_wrld_<slug>` | `geo_wrld_southamerica` |
| Geo node, country | `geo` | `geo_<iso>` | `geo_bra` |
| Geo node, inside a country | `geo` | `geo_<iso>_<slug>` | `geo_bra_sudeste`, `geo_bra_rio` |
| Club | `clb` | `clb_<iso>_<city slug>_<nnn>` | `clb_bra_rio_001` |
| Player | `plr` | `plr_<club rest>_<nnnn>` | `plr_bra_rio_001_0001` |
| Competition | `cmp` | `cmp_<locator>_<nnn>` | `cmp_bra_001`, `cmp_wrldt_001` |
| Season | `ssn` | `ssn_<competition rest>_<yyyy>` | `ssn_bra_001_2026` |

*Amended 2026-10-10.* The row "geo node, confederation" (`geo_wrld_conmebol`) became "geo node,
continent". A confederation is not a place: it is a field of the country
(`Countries.FootballConfederation`), per `GEO-D53` and ADR-0015. Migration 0020 turns
`geo_conmebol` into `geo_wrld_southamerica` (kind `Continent`, display "América do Sul").

The tool allocates every id; the author never types one. The "Nova divisão" form loses its id
field. Shapes for entities that do not exist yet follow the same rule when their sprint arrives:

- stadium `std_<iso>_<city slug>_<nnn>`;
- staff, from the club that created them (Sprint 12);
- national team `nat_<iso>`;
- free agents (Sprint 13), whose origin is the country pool and not a club.

Each sprint writes its shape into the table above.

### 4. One module builds and checks every id

`Core/World/Ids.cs` is the only place that builds an id, takes one apart, or checks one. The
generators, the pyramid editor, the readers and the migration tests all call it; no string
interpolation of an id remains anywhere else.

`Ids.Check` runs wherever `CompetitionIntegrity` already runs (ADR-0012, clarification 9): on the
JSON document, on the CSV tabs and on every undo snapshot. A document with an id outside the
convention is refused, and the message names the id and this ADR. That also refuses every
document exported before this change.

### 5. Migration 0020 renames in place

Migration 0020 rewrites every stored id and every reference to it:

- geo nodes and their parents;
- clubs' geo nodes;
- players;
- competitions with their anchors, stages and rules;
- seasons and their participants;
- the `WorldEdits.EntityId` of renamed entities, so the trail still points at them;
- the city ids inside each stored club-profiles document.

Undo snapshots are deleted, as in 0019: they hold the old ids, and the cap is 25. A rename map
that does not cover every row aborts with a named `CHECK`, using the guard-table technique from
0019.

The same migration carries the renames of ADR-0015 (`docs/RENAMES-0020.md`), so stored data is
converted once.

The fixtures (`world.json`, `club_profiles.json`) are converted once, in the same commit.

The random streams are keyed by the club id and the country code (`SquadGenerator`,
`ClubGenerator`), and neither changes, so generated attributes do not change. If a test finds an
output that does move (for example, a city order that came from sorting ids), the commit re-pins
it and says which.

## Consequences

- Every literal id in the tests changes. That is mechanical, and it happens in the commit that
  implements this.
- Old exports cannot be imported, as after 0019. Re-export after migrating.
- Competition and season ids become opaque (`cmp_bra_002`). The screens show names, and search
  finds by name. The id is for machines, and now it never lies.
