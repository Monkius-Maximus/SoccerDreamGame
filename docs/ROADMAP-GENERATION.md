# Roadmap — World generation (Sprints 10–14)

Implements [ADR-0011](adr/0011-world-generation-clubs-staff-free-agents-npcs.md) and, from
Sprint 11c, [ADR-0012](adr/0012-competitions-as-composition.md). Sprint 11d implements
[ADR-0013](adr/0013-identifier-convention.md), [ADR-0015](adr/0015-language-and-terminology.md)
and [ADR-0016](adr/0016-decision-identifiers-and-legacy-registers.md); Sprint 11e implements
[ADR-0014](adr/0014-league-strength-model.md). This picks
up where the World Builder's Sprints 0–9 ended. Every sprint ends with both test suites green
and a commit titled `World Builder Sprint N: …`.

**Owner's goal:** generate a complete game base, meaning clubs, squads, professionals and free
agents, from a few choices. The first usable batch arrives at the end of **Sprint 11**.

Rules that apply to every sprint (see `CLAUDE.md`):

- Generators are pure functions in `SoccerSim.Core`. Endpoints, CLI verbs and `app.js` stay
  thin.
- Pools, distributions and constants are imported data. Anything without a source is marked
  provisional and shows up in the audit.
- Fail fast: a missing profile, an unknown enum value or a broken invariant throws, naming what
  is missing.
- Same seed, same output. Every generator gets a determinism test (`Generate` twice → equal).

---

## Sprint 10 — A club from nothing

### 10a — Core ✔

Delivered as a patch from a session without NuGet or SQLite, applied on branch
`claude/world-builder-sprint-10`.

- `ClubIdentity.Audit` is nullable, and `ClubIdentity.Provenance` is computed from it (existing
  `Provenance` enum).
- Core handles the missing audit in `ClubFields`, `ClubInvariants` and `BatchAudit`, in the
  JSON reader and writer (explicit `"audit": null`), and in the CSV `Clubes` tab (new
  `provenance` column, no `Audit_Clubes` row for Regen).
- `KitDerivation`: the away kit by polarity inversion (pilot: 20/20 shirt and shorts, 18/20
  socks).
- `ClubProfiles` + `ClubProfilesReader`: strict, every section with a declared source, and
  `reservedNames` for real clubs' names.
- `ClubGenerator.Generate(request, profiles, geoNodes, calibration, taken, masterSeed)` and
  `ClubGenerationContext.From(clubs)/.With(club)` for batches.
- `StableHash` (FNV-1a) moved to `Core/Random`. `SquadGenerator` uses it, and its output is
  unchanged.
- Data: `tests/SoccerSim.Core.Tests/TestData/club_profiles.json` (BRA, 11 cities). Measured
  from the pilot where possible, `authored` elsewhere.
- Tests: `ClubGeneratorTests`, `KitDerivationTests`, `ClubProfilesReaderTests`,
  `RegenClubTests`.

### 10b — Persistence, CLI, API, UI ✔

- Migration `0018`: `Clubs.Provenance` (CHECK Anchored/Regen; the DEFAULT marks rows written
  before it as Anchored). `ClubRepository` writes and reads the audit row only for Anchored
  clubs, throws on read if the column and the audit row disagree, and refuses an update that
  would change a club's provenance.
- Club profiles are stored as the imported document, one row per country
  (`ClubProfileDocuments`), and read back through `ClubProfilesReader`. The document has some two
  dozen sourced sections and the generator only reads it whole, so normalising it bought
  nothing. `worldbuilder import-club-profiles <file> [db]`.
- `ClubCreation.PreviewAsync/ApplyAsync` (Core): loads the inputs, generates, and writes one
  history entry plus one edit in a transaction. The CLI and the API both call it.
- CLI: `worldbuilder generate-club <countryId> <band> <strength> <seed> [db]`.
- API: `GET /api/clubs/generate/options` (countries with profiles, bands, a fresh seed),
  `POST /api/clubs/generate/preview` and `/apply` (`ClubGenerationRequest`).
- UI: "+ Gerar clube" in the rail opens a dialog with the four choices and a preview drawn with
  the club page's own header and kit/palette cards. The club page shows a Regen badge and hides
  the audit group for a Regen club.
- A generated club is **not enrolled** in any division (owner's decision): enrol it from the
  "Ligas" tab. Until then `worldbuilder project` refuses it, as it refuses any club outside a
  national competition. Sprint 11 enrols the batch.
- Tests: `ClubProvenancePersistenceTests` (Core, SQLite) and `ClubGenerationApiTests`
  (WorldBuilder: preview writes nothing, apply persists a Regen club, undo removes it, JSON
  `"audit": null`, CSV export and re-import of a Regen club). The SQLite-backed Core tests
  that 10a could not run all pass.

## Sprint 11 — A whole division in one go (first usable batch)

### 11a — Core and data ✔ (delivered as a patch, applied on branch `claude/world-builder-sprint-11`)

- `DivisionGenerator.Generate(request, pyramid, clubProfiles, playerProfiles, country, geoNodes,
  calibration, existingClubs, masterSeed)` → `DivisionGenerationResult(Clubs, Characters,
  Pyramid)`. Pure. Each club comes from `ClubGenerator` and its squad from `SquadGenerator`, each
  club is enrolled with `PyramidEditor.Enrol`, and the context is folded so the batch never
  collides with itself or the world.
- Strength is a ladder from `strengthMax` down to `strengthMin` (two decimals). It is not
  sampled, because a seeded spread can bunch the whole field together.
- Refused before anything is drawn:
  - an unknown division;
  - more clubs than free seats;
  - a strength range outside 0 < min ≤ max ≤ 1;
  - a pyramid or country profile from another country.
- **Bug fixed on the way.** `SquadGenerator` cut club slugs to 12 characters, so two clubs in a
  long-named city (`clb_bra_saogoncalo_001`/`_002`) shared player ids. The slug is no longer cut.
  Every pilot slug is at most 9 characters, so pilot player ids are unchanged.
- **Data: more cities.**
  - The world document now has the Centro-Oeste region and 56 more cities (75 geo nodes). Rule:
    every state capital plus every municipality with at least 400,000 residents in the IBGE Censo
    2022.
  - `club_profiles.json` has 67 cities: `population2022` (IBGE), `weight = √population` (an
    authored transform, because linear weights gave São Paulo a quarter of every division), and
    `maxClubs` by population (≥5M 8, ≥2M 6, ≥1M 4, otherwise 3; 2 for the small pilot towns).
  - Districts: measured for the 11 pilot cities, an authored rule for the rest.
  - Real clubs named after the new cities (Joinville, Manaus, Brasília, …) were added to
    `reservedNames`.
  - Tests that counted 18 geo nodes now count 75.
- Tests: `DivisionGeneratorTests` (18 cases). Core suite without SQLite: 3,190 cases green. The
  SQLite and WorldBuilder suites were not run in that environment. Once applied, both suites ran:
  two more literals in `WorldJsonReaderTests` still counted 18 geo nodes and were fixed.

### 11b — Persistence, CLI, API, UI ✔

- `DivisionCreation.PreviewAsync/ApplyAsync` in Core, following `ClubCreation`:
  - load the pyramid, the club and player profiles, the country profile, the calibration and
    the master seed (a missing one throws with its remedy, the same messages as `ClubCreation`);
  - generate;
  - write clubs, characters and every division of the country in **one transaction**, with
    **one history entry** (`Gerar divisão <name> (<n> clubes)`) and one edit per club. The
    pyramid is upserted inside that transaction, not through `WorldScale.SavePyramidAsync`,
    which opens its own;
  - whole-world undo reverts the batch.
- CLI: `worldbuilder generate-division <countryId> <divisionId> <clubCount> <band> <strengthMin>
  <strengthMax> <seed> [db]`.
- API: `POST /api/countries/{countryId}/divisions/{divisionId}/generate/preview` and `/apply`.
  The dialog's bands and fresh seed come from the existing `/api/clubs/generate/options`.
- UI: "Gerar divisão" on each division row of the "Ligas" tab (disabled when the division is
  full). The preview table shows club, city, band, strength, players and XI OVR
  (`ProbableEleven`), then a confirm step.
- An existing `world.db` was imported before the 56 new cities existed. With the new
  `club_profiles.json` imported, generation there fails fast ("… which is not a City in the geo
  tree"). Delete `world.db` and re-import the three documents, or add the cities on the geo
  screen with the exact `geo_city_<slug>` ids.
- Tests: `DivisionCreationPersistenceTests` (preview writes nothing; apply persists N clubs and
  their squads, all enrolled, as one act; a failure mid-batch writes nothing; undo restores the
  previous world exactly; missing profiles throw) and `DivisionGenerationApiTests`.
- **Projection is not part of Sprint 11** (ADR-0011 amendment of 2026-10-03). The legacy
  projection builds leagues from national competitions, not divisions (ADR-0005 §5, ADR-0007
  §1), so a club enrolled only in a division cannot be projected. `worldbuilder project`
  refuses and names every such club; a test pins that refusal. The same was already true of a
  club from `generate-club`.

**Owner can now:** generate Série B/C/D (or any country with profiles), inspect the result and
export JSON/CSV. Projecting the generated divisions into the game tables waits for its own step.

## Sprint 11c — Competitions as composition (ADR-0012) ✔

Comes before the name pools and Sprint 12, because every later sprint generates into
competitions, and because the owner's generated divisions cannot reach the game until this
lands.

**Delivers:** one competition model. `Competition` (the definition) with `CompetitionStage`
(league only, 1 or 2 legs) and `TransitionRule`; `CompetitionSeason` with its participants; the
world's `CurrentSeason`; the pyramid as a computed view. `Division` and `CompetitionFormat` are
removed.

- **Core:**
  - The records of ADR-0012 §2.
  - `LeaguePyramid` is computed from competitions with a level.
  - `PyramidRules` uses the renamed codes (ADR-0012 §6) plus `STAGE_COUNT`,
    `TRANSITION_RANGE`, `TRANSITION_OVERLAP` and `TRANSITION_TARGET`.
  - `PyramidEditor` writes the promotion and relegation rules in pairs from one exchange number.
  - The tier float is derived from the season (§7), and rounds and matches from the stage (§4).
- **Generation:**
  - `DivisionGenerator`'s request names a `CompetitionId`, and the clubs it generates are
    appended to the current season's participants.
  - `DivisionCreation` writes the clubs, the squads and the participants in one transaction,
    as before.
- **Projection:** leagues come from the current seasons of national leagues (§8).
  `worldbuilder project` succeeds after `generate-division`. The Sprint 11 refusal test becomes a
  success test.
- **Persistence:** migration `0019_competitions_as_composition.sql`, exactly as ADR-0012 §11
  describes: conversion, the two abort cases, and clearing the pre-0019 undo snapshots.
- **Exchange:**
  - The JSON document carries `competitions` (with stages and rules), `seasons` and
    `meta.currentSeason`, and an old-shape document is refused by name.
  - The pilot fixture is converted once.
  - CSV tabs follow the new shape (name the new tabs in the plan).
  - `Scale` keeps countries only.
- **UI:** the "Ligas" tab shows the computed pyramid with the current season's participants, and
  the exchange between adjacent levels is edited as one number. The competition card in search
  shows derived rounds.
- **Tests:**
  - The pilot converts to Série A 2026 with 20 participants, a tier float of 0.86, and 38 rounds
    and 380 matches derived.
  - The pair-written rules keep `PYRAMID_FLOW` clean.
  - Migration 0019 over a database with the pilot and an empty tier-1 division keeps the pilot
    and drops the division; with a non-empty one, it aborts and writes nothing.
  - Generating a division and then projecting succeeds.
  - An old-shape document is refused.
  - Undo works across a generation after 0019.

**Before applying 0019 to an existing `world.db`:** in "Ligas", make sure no tier-1 division in
a country with an imported league has clubs (ADR-0012 §11 step 5).

**Done** (owner's decisions recorded in ADR-0012's Clarifications):

- `PromotedIn` keeps the code's meaning in 0019 (clubs coming up into a level), so the pilot's 4
  and 4 above a level 2 become a balanced pair. Level 1's counts are the imported league's.
- A new level's geo anchor is chosen by the author (a `Country` node); 0019 takes it from the
  country's national competition, and aborts if there is none.
- Only the `Division` record left the Core; `DivisionGenerator` and `DivisionCreation` keep their
  names, and the request names a `CompetitionId`.
- 0019 also refuses ambiguous rounds, a world without a competition to take the season from, a
  national competition with no members and two national competitions in one country.
- CSV tabs: `Competicao`, `Fases`, `Temporadas`, `Transicoes`.
- `generate-division` followed by `project` writes one legacy league per level, with its tier
  from the season's quality (pilot 0.86 → 1, a 0.71 Série B → 2, a 0.54 Série C → 3).

### Moved out of Sprint 11: name pools per nationality

They were listed as a prerequisite, but they turned out not to be one. With 108 first names
and 338 surnames (~36,000 combinations) a 20-club division does not run out of names. What is
missing is realism: an Uruguayan named "João Silva". That needs sourced pools per nationality
and a `GenerationProfiles` change that touches persistence (a new migration, 0020 or later). It is its own step,
after Sprint 11 and before the staff (Sprint 12), which will reuse the same pools.

## Sprint 11d — One id convention and one vocabulary (ADR-0013, ADR-0015, ADR-0016)

Ids and names change together because both rewrite stored data. One migration, 0020, converts
every stored id (ADR-0013) and every renamed value, key, column and table (ADR-0015) at once.
The exact list is [`docs/RENAMES-0020.md`](RENAMES-0020.md); every new name is already in
[`docs/TERMS.csv`](TERMS.csv). Commit title: `World Builder Sprint 11d: …`.

**Delivers:** one id shape for every entity, built and checked in one module; English names
for every stored value; and the confederation moved out of the geographic tree (`GEO-D53`).

- **Core:**
  - `World/Ids.cs` builds, parses and checks every id (ADR-0013 §4). The generators, the pyramid
    editor and the readers call it, and no id is built by string interpolation anywhere else.
    `Ids.Check` runs wherever `CompetitionIntegrity` already runs.
  - Code renames: `RENAMES-0020.md` §A (types and members behind the stored values) and §B
    (code-only names).
  - `GEO-D53`:
    - `GeoNodeKind.Confederation` becomes `Continent`;
    - a new `FootballConfederation` enum becomes a field of the country;
    - the parent rules in `GeoTree` and `BatchAudit` follow.
- **Persistence:** `sql/0020_identifier_convention.sql`:
  - the id rename map of ADR-0013 §5;
  - the value, column and table renames of `RENAMES-0020.md` §A;
  - `Countries.FootballConfederation` (`BRA` = `CONMEBOL`).

  A row the rename map does not cover aborts the migration with a named `CHECK`, using the
  guard-table technique from 0019. Columns with a `CHECK` are renamed by rebuilding the table,
  as 0019 did. Undo snapshots are cleared.
- **Exchange:**
  - JSON keys, CSV tab names and CSV columns follow `RENAMES-0020.md` §A4–§A8.
  - A document in the old shape (for example one with `prestigeBand`, `Titular`, `tema` or a
    `Clubes` tab) is refused by name.
  - The fixtures (`world.json`, `gen_profiles.json`, `club_profiles.json`) are converted once,
    in the same commit.
- **UI:**
  - The tool text that names a renamed concept changes (`RENAMES-0020.md` §C).
  - The "Nova divisão" form loses its id field (ADR-0013 §3).
- **Docs:** every unprefixed `D-NN` citation in code and ADRs is rewritten (`RENAMES-0020.md`
  §D, ADR-0016 §5). `CLAUDE.md`'s rules that name a renamed symbol are updated too: Anchored vs
  Regen, the `generate-division` example, and the CSV tab names in the IP-hygiene rule.
- **Tests:**
  - Every shape in ADR-0013 §3 round-trips through `Ids`. A malformed id is refused, and the
    message names the id and ADR-0013.
  - Migration 0020, over a database with the pilot and a generated division, converts every id
    and value. A row outside the rename map aborts the migration and writes nothing.
  - Old-shape JSON and CSV are refused by name.
  - The golden gate still holds after conversion: 688/688 players with the same overall, market
    value and salary. Renaming must not move a number.
  - Determinism: `SquadGenerator` and `ClubGenerator` keep their output, because they are keyed
    by club id and country code. `DivisionGenerator` is keyed by `CompetitionId`, which changes
    (`cmp_bra_tier2` → `cmp_bra_00N`), so a generated division moves. Re-pin those tests and
    name them in the commit.
  - `FrontEndSmokeTests` keeps its anchors.

**Not in 11d:**
- moving the tool's strings out of `app.js` and Core (ADR-0015 §5, its own sprint);
- the findings in `RENAMES-0020.md` §E;
- ADR-0014 (Sprint 11e);
- importing the legacy registers into `docs/legacy/`, which the owner supplies.

**After applying 0020 to an existing `world.db`:** re-export. An export written before 0020
cannot be imported.

## Sprint 11e — League strength (ADR-0014)

Generating a division asks only for the competition, the count and the seed. Strengths come from
a measured profile through the Opta bridge, and a generated club's reputation is derived from
its strength. **Blocked by the strength pack** (`strength_profiles.json`), which needs the owner's
captures of the Opta Power Rankings.

## Sprint 12 — Staff (coach first)

**Delivers:** `StaffRecord`, `StaffRole`, `StaffAttr`, `StaffGenerator.GenerateForClub(club,
staffProfiles, calibration, masterSeed, seed)`.

- Confirm the `StaffAttr` list with the owner at kickoff (ADR-0011 §4 proposes 14).
- Data: `staffShape` (count per role), attribute distributions per role (provisional), the
  role-weight matrix, and the salary curve by role and band.
  `worldbuilder import-staff-profiles <file> [db]`.
- Coaches get `PreferredTacticalStyle`, biased toward the club's `DefaultTacticalStyle`, and a
  `PreferredFormation`.
- Every staff member gets an `Archetype`: an id into archetypes defined as **data** in the staff
  profiles, each a set of decision weights (provisional, and audited as such). The tool only
  assigns it; the game's AI reads the weights (ADR-0012 §1). Confirm the archetype list with the
  owner at kickoff, together with the `StaffAttr` list.
- `DivisionGenerator` also generates staff. Existing clubs get staff through
  `POST /api/clubs/{clubId}/staff/generate` (preview/apply), plus a batch action for "every club
  without staff".
- Persistence: `Staff` and `StaffAttributes` migrations. CSV tab `Comissao`. JSON carries
  `staff`.
- Game: a legacy `Managers(TeamId, Name, TacticalStyle, Rating)` table, filled by
  `worldbuilder project` from each club's `HeadCoach`. That is all non-career modes need.
- UI: a "Comissão" block on the club page (list, edit, regenerate).

**Tests:** determinism; every club has exactly one `HeadCoach` after generation; weights sum to
1 per role (audit); the projection writes one manager per team.

## Sprint 13 — Free agents

**Delivers:** nullable `ClubId`/`ShirtNumber`/`SquadRole` on `CharacterRecord`, nullable
`ClubId` on `StaffRecord`, and `FreeAgentGenerator`.

- Invariant: those three fields are all null or all set. `WorldDerivations` prices free agents
  with `freeAgentPrestigeBand` (calibration, provisional).
- Pool: `DivisionGenerator` adds `round(clubCount × squadSize × freeAgentPoolRatio)` free
  players plus free staff.
- On demand: `FreeAgentGenerator.Generate(filter{position, ageMin, ageMax, ovrMin, ovrMax,
  nationality?}, count, seed)`. CLI `worldbuilder generate-free-agents …`, API preview/apply,
  and a "Livres" filter plus a "Gerar livres" action in the Register view.
- Projection: free players go to `Players` with `TeamId = NULL`.
- Update the existing tests that assumed a non-null `ClubId` deliberately, and say so in the
  commit.

**Tests:** the invariant throws on a partial null; the pool size matches the ratio; the filter
is respected; the projection writes free agents with a null team; the CSV round-trip keeps
free agents.

## Sprint 14 — Career NPCs (contract and generator only)

**Delivers:** `SoccerSim.Core/People/NpcGenerator.At(masterSeed, cityGeoNodeId,
districtArchetype, slot) → NpcRecord`, and the promotion contract.

- `NpcRecord` (minimum): `NpcId` (derived from the key), names from the country pools, age,
  occupation (data pool per `DistrictArchetype`), home geo node, `AppearanceSeed`, and
  `PersonalitySeed` (the hook for the ProtoChatBot compatibility system later).
- Nothing is stored for generic NPCs. `INpcPromotionStore` (a Core port) persists only promoted
  NPCs and their mutable state in the **career save**, not in the world database.
- No World Builder UI. The life-sim scene is the consumer, and its spec decides any extra fields.

**Tests:** the same key gives the same NPC; different slots give different NPCs; a promoted
NPC's saved state overrides the generated defaults; generation reads no database.

---

## Out of scope (future ADRs)

Contracts and expiry dates, and in-game transfers and signing of free agents. Packaging the
world for a game build (stripping audit and anchor data per the IP rule) also needs its own
ADR.

### Game-side epics (outside the World Builder, ADR-0012 §1)

The tool authors the structure and the game plays it. These belong to the game and each needs its
own ADR there:

- Stages beyond the league: knockout, Swiss and groups, with points, extra time, penalties,
  tie-breakers, play-offs and carried points. The tool gains the matching stage kinds when the
  first cup or continental competition is authored.
- Fixtures (round-robin pairing) and the calendar that slots matchdays.
- End of season: applying transition rules and creating the next season.
- Finances at runtime.
- Academy intake, which may use provisional potential ranges as data.
- Transfer and market AI. It reads the staff archetypes from Sprint 12 and keeps
  `WorldEconomy.MarketValueEur` as the one value formula.
