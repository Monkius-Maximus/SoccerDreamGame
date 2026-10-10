# Renames for Sprint 11d — migration 0020

Source: ADR-0015 (terminology) and ADR-0016 (decision ids), decided 2026-10-10. This list feeds
Sprint 11d together with ADR-0013 (identifiers). Every renamed term is already in `docs/TERMS.csv`.

The counts come from commit `7ec9d3e` (merge of PR #9): occurrences / files, across `src`,
`tools`, `tests`, `sql`, `game` and `docs`. They size the work; they are not a checklist.

**Rule for 11d:** one migration, 0020, converts every stored value below together with the
ADR-0013 ids. The fixtures (`world.json`, `gen_profiles.json`, `club_profiles.json`) are
converted in the same commit. An export written before 0020 cannot be imported (as ADR-0013
already states).

---

## A. Renames that rewrite stored data

### A1. `SquadRole` values

| Old | New | Label pt-BR / en-US |
| --- | --- | --- |
| `Titular` | `Starter` | Titular / Starter |
| `Rotacao` | `Rotation` | Rotação / Rotation |
| `Reserva` | `Backup` | Reserva / Backup |
| `Promessa` | `Youth` | Promessa / Prospect |

Where it lives:
- the enum in `World/Enums.cs`;
- the `CHECK` on `Characters.SquadRole` (0009);
- `players[].squadRole` in `world.json` (688 rows);
- the CSV `Players` tab;
- the generators that assign roles (`SquadRole.` 23 / 10).

`Youth` and not `Prospect`: `Prospect` is already the age phase 15–20.

### A2. `Provenance.Regen` → `Generated`

Where it lives:
- the `CHECK` on `Characters.Provenance` (0009) and on `Clubs.Provenance` (0018), whose
  `DEFAULT 'Anchored'` stays;
- `"Regen"` in fixtures (407 / 5);
- the symbol `Regen` (525 / 33, mostly comments and test names: rename those too).

Tool text: "nasce Regen" → "nasce gerado", and the `tag-regen` class → `tag-generated`.

### A3. Prestige band → club reputation

| Old | New |
| --- | --- |
| `enum PrestigeBand { B1…B6 }` | `enum ClubReputation { World, Continental, National, Regional, Local, Modest }` (B1→World … B6→Modest) |
| `ClubIdentity.World.PrestigeBand`, JSON `world.prestigeBand` | `Reputation`, `world.reputation` |
| `Clubs.PrestigeBand` + `CHECK` + `IX_Clubs_PrestigeBand` (0007) | `Clubs.Reputation` + `CHECK` + `IX_Clubs_Reputation` |
| `PrestigeBandCalibration`, `WorldCalibration.Bands` | `ReputationCalibration`, `WorldCalibration.ByReputation` |
| Table `CalibrationPrestigeBands` | `CalibrationReputations` |
| JSON `calibration.bands` | `calibration.byReputation` (keys `World`…`Modest`) |
| `SquadSizeByBand`, `squadSizeByBand` | `SquadSizeByReputation`, `squadSizeByReputation` |
| `bandValueMult` (API and UI) | `reputationValueMult` |

Counts:
- `PrestigeBand` 109 / 44;
- `prestigeBand` 42 / 13;
- `PrestigeBandCalibration` 13 / 8;
- `CalibrationPrestigeBands` 5 / 3;
- `SquadSizeByBand` 7 / 6.

The `generate-division … B4 …` argument takes the new values until ADR-0014 removes it in
Sprint 11e.

Tool text:
- "Banda de prestígio" → "Reputação";
- "Banda B2" → "Reputação Continental";
- "Todas as bandas" → "Todas as reputações";
- the column "Banda" → "Reputação".

### A4. Sources: `Tema`, `Numero`, `Fonte`

| Old | New |
| --- | --- |
| `record WorldSource(Tema, Numero, Fonte, Url)` | `WorldSource(Topic, Claim, Citation, Url)` |
| Table `WorldSources (Tema, Numero, Fonte, Url)` (0010) | `WorldSources (Topic, Claim, Citation, Url)` |
| JSON `sources[].tema`, `numero`, `fonte` | `topic`, `claim`, `citation` |
| CSV `Sources` columns `tema;numero;fonte;url` | `topic;claim;citation;url` |
| Data-pack contract (ADR-0014 §4, `name_pools.json`): `source: { fonte, url, accessed }` | `source: { citation, url, accessed }` (packs outside the repository too) |

Counts: `Tema` 54 / 15; `Numero` 43 / 12; `Fonte` 47 / 13.

### A5. `Uf` → `RegionCode`

Where it lives:
- the `Clubs.Uf` column (0007);
- `ClubIdentity.Geography.Uf`;
- the JSON `geography.uf` → `geography.regionCode`;
- the field path `geography.uf` → `geography.regionCode`;
- the CSV column.

Counts: 118 / 18. Tool label: "UF" → "Região".

### A6. Money keys

The JSON and CSV keys follow the C# and SQL casing, which is already `MarketValueEur` and
`SalaryMonthlyBrl`:
- `marketValueEUR` → `marketValueEur` (693 / 5);
- `salaryMonthlyBRL` → `salaryMonthlyBrl` (692 / 5).

### A7. Confederation leaves the geographic tree (`GEO-D53`)

This is not a rename: it carries out a decision the code never followed. It goes into 0020
because 0020 already rewrites every geo id.

- `GeoNodeKind.Confederation` → `Continent`:
  - the enum;
  - the `GeoTree` parent rules;
  - the `BatchAudit` parent table.
- Node `geo_conmebol` (kind `Confederation`, display "CONMEBOL") → `geo_wrld_southamerica` (kind
  `Continent`, display "América do Sul"). ADR-0013 §3: the row "geo node, confederation" becomes
  "geo node, continent" (`geo_wrld_<slug>`).
- New `enum FootballConfederation { AFC, CAF, CONCACAF, CONMEBOL, OFC, UEFA }` and a new column
  `Countries.FootballConfederation NOT NULL` (BRA = `CONMEBOL`).
- Competitions with `Continental` scope filter by the country's confederation, not by a node.
  None exists yet.

Counts: 9 / 4.

### A8. CSV tabs and columns

ADR-0006 §1 is amended: the export uses English names.

| Old tab | New tab |
| --- | --- |
| `Leia-me` | `Readme` |
| `Clubes` | `Clubs` |
| `Competicao` | `Competitions` |
| `Fases` | `Stages` |
| `Temporadas` | `Seasons` |
| `Transicoes` | `Transitions` |
| `Jogadores` | `Players` |
| `Kits_Estadio` | `KitsStadiums` |
| `Audit_Clubes` | `ClubAudit` |
| `Audit_Jogadores` | `PlayerAudit` |
| `Calibracao` | `Calibration` |
| `Fontes` | `Sources` |
| `Paises` | `Countries` |
| `Pesos_Posicao` | `PositionWeights` |
| `GeoNodes` | stays |

Column renames:
- `participantes` → `participants`;
- `secao`/`texto` → `section`/`text`;
- `crest_originalChargeReplaced`/`crest_sourceCitation`/`crest_substituteCharge` →
  `crestOriginalChargeReplaced`/`crestSourceCitation`/`crestSubstituteCharge` (75 / 7).

Counts for the tab names: 138 / 13. The `Readme` tab's own content stays pt-BR: it is tool text.
It changes "Terra Paralela — Base de Mundo" to "Terra — Base de Mundo" and quotes the new tab
names.

---

## B. Code-only renames (no stored data)

| Old | New | Count | Note |
| --- | --- | --- | --- |
| `CompetitionStages.TierFloat`, DTO `TierFloat`, API/UI `tierFloat`, comments `leagueTierFloat` | `CompetitionStages.Strength`, `CompetitionStrength`, `competitionStrength` | 33 / 15 | Derived, never stored. The tool's column "Força" stays |
| `WorldToLegacyProjection.TierFor(double leagueTierFloat)` | `SimulationTierFor(double competitionStrength)` | 4 / 2 | |
| `SimulationTier { ActiveHuman = 1, MajorForeign = 2, Minor = 3 }` | `{ Full = 1, Statistical = 2, Aggregate = 3 }` | 42 / 16 | `Leagues.Tier` keeps its integers 1–3 |
| `EventTier { High, Medium, Low }` | `EventStakes { High, Medium, Low }` | 14 / 5 | |
| `SourceSeal.Anchored` | `SourceSeal.Sourced` | 2 / 2 | Calculated, not stored |
| Test names containing `Tier`, `Regen`, `Band` | Same with the new term | — | e.g. `ThePilotsTierFloat_IsDerivedFromItsParticipants` |
| `Division*` types and `generate-division` | Unchanged | 150 / 15 | "Division" is a role (ADR-0015 §4) |

## C. Tool screen text (pt-BR, only where it names a renamed concept)

| Where | Old | New |
| --- | --- | --- |
| `ClubFields.cs` | "Alcunha", "Alcunha da âncora", "Alcunha registrada (1/0)" | "Apelido", "Apelido da âncora", "Apelido registrado (1/0)" |
| `ClubFields.cs` | "Mote" | "Lema" |
| `ClubFields.cs` | "UF" | "Região" |
| `ClubFields.cs`, `app.js` | "Banda de prestígio", "Banda", "bandas" | "Reputação", "reputações" |
| `app.js` | "Regen" in prose | "gerado" |
| `app.js` (history screen) | "Edição", "edições" (14 / 2) | "Alteração", "alterações" |

Moving every tool string out of `app.js` and Core is **not** part of 11d (ADR-0015 §5).

## D. Documentation

- **`CLAUDE.md`.** The language line and the layout table were updated together with these
  ADRs. Sprint 11d updates the rules that name a renamed symbol: "Anchored vs Regen", the
  `generate-division … B4 …` example, and the tab names in the IP-hygiene rule
  (`Audit_Clubes`, `Audit_Jogadores`).
- **Decision citations (ADR-0016 §5).**
  - `D-38`/`D-39` in code and comments → `CAL-D38`/`CAL-D39` (9 / 4).
  - `D-01`…`D-04` in ADRs and code → `WBH-D01`…`WBH-D04` (14 / 9).
- **Already committed with these ADRs:** ADR-0013 (with the continent row), ADR-0014 (with the
  reputation amendment), ADR-0015, ADR-0016, `docs/TERMS.csv` and `docs/decisions/LEGACY-IDS.md`.
- **Still to come, outside 11d:** `docs/legacy/`, with the pt-BR registers as they are, and
  `docs/legacy/versao-b/`. The owner supplies the files.

## E. Findings outside this list

Each needs its own decision or sprint.

1. **`CAL-D39`.** The club generator still draws stadium capacity from the band's
   `CapMean`/`CapSd`. The decision is the country's stadium profile, shifted by reputation.
2. **`displayCode` uniqueness.** `WORLD-D20` says per country, the club-identity document (§9.1)
   says global, and SQL implements global.
3. **`RegionCode`.** It repeats what the geo tree already knows (the club's city node has a
   region parent). Removing it is a separate decision.
4. **`SimCore`.** `SimCore.cs` in the design documents is not this repository's core. Ownership
   is listed as `decide` in `docs/TERMS.csv`.
5. **Title of the game the player sees.** Still open. "Terra" is the world's internal name only.
