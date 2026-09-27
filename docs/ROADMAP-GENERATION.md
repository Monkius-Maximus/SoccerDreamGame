# Roadmap — World generation (Sprints 10–14)

Implements [ADR-0011](adr/0011-world-generation-clubs-staff-free-agents-npcs.md). This picks
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

**Delivers:** `DivisionGenerator.Generate(country, divisionId, clubCount, band, strengthMin,
strengthMax, seed)` → clubs + squads (existing `SquadGenerator`, `SquadGenerationOptions.For`).

- Strength is spread across `[strengthMin, strengthMax]` and each club's squad targets its own
  strength.
- The same division never repeats a city more times than the city pool allows, and never
  repeats a display code.
- **Data prerequisites:**
  - **More cities.** The 11 profile cities have room for only about 28 more clubs
    (`maxClubs`), and every new city needs a geo node first.
  - **Name pools per nationality**, replacing the single 108/338 pool, with the source
    recorded. `GenerationProfiles` gains `NamesByNationality`. A nationality in the
  country mix with no pool throws.
- Writing: preview first, then apply as one transaction. Every club goes through
  `PyramidEditor.Enrol`. One history entry covers the batch, and whole-world undo reverts it.
- CLI: `worldbuilder generate-division <countryId> <divisionId> <clubCount> <band> <strengthMin>
  <strengthMax> <seed> [db]`.
- API: `POST /api/countries/{countryId}/divisions/{divisionId}/generate/preview` and `/apply`.
- UI: a "Gerar divisão" action on the pyramid screen (the "Ligas" tab), with a preview table (club, city, band,
  strength, XI OVR) and a confirm step.

**Tests:** determinism; count and enrolment are correct; a failure mid-batch writes nothing;
undo restores the previous world exactly; after apply, `worldbuilder project` succeeds.

**Owner can now:** generate Série B/C/D (or any country with profiles), inspect the result,
export JSON/CSV and project it into the game tables.

## Sprint 12 — Staff (coach first)

**Delivers:** `StaffRecord`, `StaffRole`, `StaffAttr`, `StaffGenerator.GenerateForClub(club,
staffProfiles, calibration, masterSeed, seed)`.

- Confirm the `StaffAttr` list with the owner at kickoff (ADR-0011 §4 proposes 14).
- Data: `staffShape` (count per role), attribute distributions per role (provisional), the
  role-weight matrix, and the salary curve by role and band.
  `worldbuilder import-staff-profiles <file> [db]`.
- Coaches get `PreferredTacticalStyle`, biased toward the club's `DefaultTacticalStyle`, and a
  `PreferredFormation`.
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
