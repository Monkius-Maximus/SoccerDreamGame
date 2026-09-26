# ADR-0011 — Generating the world: clubs from scratch, staff, free agents and career NPCs

- Status: Accepted (Sprint 10a implemented in Core)
- Date: 2026-09-25, amended 2026-09-26
- Applies to: `src/SoccerSim.Core/World/Generation/`, `src/SoccerSim.Core/World/`,
  `sql/`, `tools/SoccerSim.WorldBuilder/`, and (NPCs only) the career save schema
- Plan: `docs/ROADMAP-GENERATION.md` (Sprints 10–14)

## Context

Sprints 0–9 made the World Builder the source of truth for an **authored** world: import the
20-club pilot league, edit it, generate a squad for a club that already exists
(`SquadGenerator`), and project it onto the game tables (`worldbuilder project`).

The owner now needs to **populate the game base**, not only curate it. Today the tool cannot:

1. Create a club from nothing. New clubs only enter through a CSV import where every field is
   typed by hand. The prototype's "Novo clube" wizard was never ported.
2. Create many clubs at once (a whole division).
3. Represent anyone who is not a player. There is no coach, staff or board entity.
4. Represent a free agent. `CharacterRecord.ClubId` is required, and `WorldDerivations`
   prices every player through `bands[player.ClubId]`.
5. Populate the career-mode world with non-football people.

The owner decided (2026-09-25):

- **Professionals:** head coach, coaching staff (assistant, fitness coach, goalkeeper coach),
  medical/scouting (doctor, physio, scout) and board (director of football, president). In the
  non-career modes only the **head coach** matters.
- **Free agents:** both an initial pool generated with the world and on-demand generation.
- **First batch:** a **whole division generated in one go**.
- **Career mode:** NPCs populate maps and cities. Only NPCs the player interacts with, plus a
  few map-local generics, are persisted, in the spirit of The Sims 4 townies.

## Decisions

### 1. Every generator is a pure function in Core

`ClubGenerator`, `StaffGenerator`, `FreeAgentGenerator`, `DivisionGenerator` and
`NpcGenerator` live in `SoccerSim.Core/World/Generation/` (NPCs in `SoccerSim.Core/People/`).
Each one follows `SquadGenerator`'s contract: no database, no clock, no ambient state, and the
same arguments always return the same records. The World Builder only adds endpoints, CLI
verbs and UI. The game calls the same functions. This is what makes "the tool's logic inside
the game" true by construction rather than by duplication.

Randomness uses `DeterministicRng.CreateStream(masterSeed, …)` with one named stream per
concern. `SquadGenerator` already seeds from `(masterSeed, Hash(clubId), Hash("squad"),
seed)`; the new ones are `"club"`, `"staff"`, `"freeagents"` and `"npc"`, keyed by the entity
or place id. Regenerating one club never shifts another. The FNV-1a `Hash(string)` that is
private to `SquadGenerator` today moves to `Core/Random` so every generator shares it, and the
squad's existing output is unchanged.

### 2. A generated club is a Regen club, and its audit is absent rather than faked

`ClubDeviationAudit` describes a deviation from a real anchor. Almost every field in it
(`AnchorClubName`, `FoundingSourceCitation`, …) is meaningless for an invented club, and
filling them with placeholders would make the IP audit lie. Therefore:

- `ClubIdentity.Audit` becomes nullable.
- `ClubIdentity.Provenance` is **computed** from it (`Audit is null ⇒ Regen`), reusing the
  existing `Provenance { Anchored, Regen }` enum the players already use. It is not stored
  next to the audit, so the two can never disagree. This mirrors the player side, where a Regen
  player carries no anchor (`CharacterDeviationAudit`, ALGORITHMS.md §6.10).
- Everything in Core that assumed an audit now handles its absence:
  - `ClubFields`: the `audit.*` fields read as empty, and writing one throws
    `FieldPatchException`.
  - `ClubInvariants`: `ANCHOR_VERIFIED` does not apply to a Regen club.
  - `BatchAudit`: the founding and citation checks are skipped, because a Regen club's numbers
    come from the club profiles, whose sources travel with that file.
- **JSON:** `"audit": null` must be written explicitly. A missing key is still an error, so a
  forgotten audit is never read as Regen.
- **CSV:** `Clubes` gains a `provenance` column, and a Regen club has no `Audit_Clubes` row.
  - An audit row for a Regen club is refused.
  - Changing provenance on import is refused, because it would invent or discard an anchor.
  - A new Regen club imports with `Clubes` and `Kits_Estadio` alone.
  - Exports from before this change (no `provenance` column) no longer import. Clarity over
    compatibility: re-export them.
- **SQLite (Sprint 10b):** the database stores the fact explicitly in `Clubs.Provenance`
  (CHECK Anchored/Regen). The repository throws if an Anchored club has no audit row, or a Regen
  club has one. A missing row alone must never decide provenance, because it can also mean
  corruption.

### 3. What a club generator decides, and where each value comes from

| Field group | Source | Provenance |
| --- | --- | --- |
| `PrestigeBand`, `ClubStrength` range | **Input** to the batch (the owner chooses the division's level) | Authored |
| City (`GeoNodeId`, `CityName`, `Uf`) | Drawn from the country's geo tree, weighted by a city pool (data) | Sampled |
| Name, short name, nickname, display code | Short name: the city if free, else a district qualifier alone ("Operário"), then with the UF ("Operário-PR"), then with the city. The official name adds a club-type prefix. Real clubs' names (`reservedNames`) are never taken. The nickname comes from the palette's colour pair ("Os Rubro-Negros"). `NamingRule = Toponymic` | Sampled |
| Founding year, district archetype | Distributions per country (data) | Sampled |
| Palette, crest colours, kits | Sampled palette (each slot ΔE ≥ threshold from the others), home kit = primary/secondary/weighted socks, away kit from the new pure `KitDerivation` (polarity inversion, ALGORITHMS.md §7.2). The pilot matches it on shirt and shorts in 20/20 and on socks in 18/20. The kit must satisfy `DeltaE ≥ deltaEThreshold` | Sampled / Derived |
| Crest shape, central charge, motto | Pools (data) | Sampled |
| Stadium capacity | `CalibrationPrestigeBands.CapMean/CapSd` clamped by `CalibrationStadiumProfiles` | Sampled |
| Stadium name, atmosphere, surface | Pools/distributions (data) | Sampled |
| Tactical style | Sampled | `TacticalStyleProvenance.Sampled` |
| Home advantage, luminance, ΔE, polarity | `WorldDerivations.Recalculate` | Derived / Calculated |

Pools and distributions are **imported data** (`club_profiles.json`, `import-club-profiles`),
never literals. This is the same rule `GenerationProfiles` already follows. A country without
club profiles cannot be generated, and the tool throws with the reason, the same way
`SquadGenerator` throws for a country without a nationality mix.

### 4. Staff is its own record, not a `CharacterRecord`

Players and professionals share a name and a birth date, and almost nothing else. Overloading
`CharacterRecord` would put 12 football attributes and a shirt number on a club president.

- New `StaffRecord`: `StaffId`, `ClubId?` (null means free agent), `Role`, names,
  nationality, date of birth and age, `Attrs`, `Overall` (Calculated), `SalaryMonthly`
  (Calculated), `Provenance` (always `Regen` for generated staff). Coaches also carry
  `PreferredTacticalStyle` and `PreferredFormation`.
- New closed enum `StaffRole`: `HeadCoach`, `AssistantCoach`, `FitnessCoach`,
  `GoalkeeperCoach`, `Doctor`, `Physio`, `Scout`, `DirectorOfFootball`, `President`.
- New closed enum `StaffAttr` (1–99). The proposed set, to be confirmed at the Sprint 12
  kickoff: `TacticalKnowledge`, `Motivation`, `ManManagement`, `Discipline`,
  `AttackingCoaching`, `DefendingCoaching`, `GoalkeeperCoaching`, `FitnessCoaching`,
  `Medicine`, `Physiotherapy`, `JudgingAbility`, `JudgingPotential`, `Negotiation`,
  `FinancialManagement`.
- `Overall` per role comes from a **role-weight matrix** stored as data, the same way
  `PositionWeights` works for players. The staff count per role per club is also data
  (`staffShape`).
- **The world generates the full staff for every club, in every mode.** Modes only choose what
  they read: non-career modes read only `HeadCoach`, career reads everything. One generation
  path, and no "career-only" fork in the data.
- Staff attribute distributions have no measured source yet. They are imported as data marked
  **provisional** and appear in the Sprint 8 audit ("no number without a source") until they
  are calibrated.

### 5. A free agent is a record with no club, and nothing else changes meaning

- `CharacterRecord.ClubId`, `ShirtNumber` and `SquadRole` become nullable together.
  **Invariant:** all three are null, or none is. Anything else throws.
- `WorldDerivations` must price a clubless player. A free agent is valued at the calibration
  constant `freeAgentPrestigeBand` (data, provisional). There is no implicit default band.
- The legacy projection writes free agents with `Players.TeamId = NULL`. The column is
  already nullable (`sql/0001_initial_schema.sql`).
- **Pool:** `DivisionGenerator` also generates `round(clubCount × squadSize ×
  freeAgentPoolRatio)` free players and a proportional number of free staff.
  `freeAgentPoolRatio` is a calibration constant (data, provisional).
- **On demand:** `FreeAgentGenerator.Generate(filter, count, seed)`, where the filter is
  position, age range, OVR range and nationality. It is exposed as a CLI verb, an endpoint and a
  button in the Register view.

### 6. A division is generated and written as one unit

`DivisionGenerator.Generate(country, divisionId, clubCount, band, strengthMin, strengthMax,
seed)` returns clubs, squads (through the existing `SquadGenerator`), staff and the free-agent
pool. Writing it is all-or-nothing, goes through `PyramidEditor.Enrol` for every club, records
one history entry, and is covered by the existing whole-world undo (ADR-0008). A preview is
always shown first, the same pattern as the CSV import in ADR-0006.

### 7. Career NPCs are a function of the place, not rows in the world

Generic NPCs are **not** stored in the World Builder database and **not** stored in the save
until they matter:

- `NpcGenerator.At(masterSeed, cityGeoNodeId, districtArchetype, slot)` always returns the same
  NPC for the same place and slot. A map therefore does not need to store its townies, because
  it can regenerate them.
- The first time the player interacts with an NPC, the game **promotes** it: it writes the
  NPC's key and its mutable state (relationship, memory flags) into the career save. From then
  on, the save overrides the generated defaults.
- The fields and the save table are defined together with the life-sim spec. Sprint 14 delivers
  the generator, the promotion contract and tests only, with no UI.

## Options considered

| Option | Verdict |
| --- | --- |
| Generate in Python/JS outside Core and import the JSON | Rejected. It duplicates the logic ADR-0001 put in Core, and the game could not generate at runtime. |
| Free agents as a sentinel club (`clb_free`) | Rejected. A fake club leaks into standings, the projection and every count. Null plus an invariant says what it means. |
| Staff as `CharacterRecord` with a role flag | Rejected. It mixes two attribute schemas in one record (see §4). |
| Store every NPC in the save | Rejected. The save grows with map size rather than with what the player did. Determinism makes it unnecessary. |
| Generate non-coach staff only in career mode | Rejected. It creates two world shapes. Generating everything and letting modes read a subset keeps one path. |

## Consequences

- New migrations: club provenance and nullable audit, nullable `ClubId`/`ShirtNumber`/
  `SquadRole`, `Staff` and `StaffAttributes`, `StaffRoleWeights`, club profiles, and the new
  calibration constants. A legacy `Managers` table is added for the head-coach projection.
- CSV gains the `Comissao` tab (staff). The `Clubes` and `Jogadores` tabs accept empty club
  fields for free agents. JSON export and import carry staff.
- Existing tests that assume `ClubId` is non-null must be updated deliberately, not relaxed.
- Name pools (108 first names and 338 last names today) are too small for several divisions.
  Pools per nationality become a data prerequisite of Sprint 11, with the source recorded.
- Open for later ADRs: contracts and expiry dates, and transfers of free agents in-game.
