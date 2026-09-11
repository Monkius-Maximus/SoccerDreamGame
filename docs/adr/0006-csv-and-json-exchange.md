# ADR-0006: The CSV and JSON exchange, and why an import shows a diff first

- Status: Accepted
- Date: 2026-09-11
- Applies to: `src/SoccerSim.Core/World/Serialization/Csv*.cs`, `World{Csv,Json}*.cs`,
  `src/SoccerSim.Core/World/Import/WorldStore.cs`,
  `tools/SoccerSim.WorldBuilder/Api/ExchangeEndpoints.cs`

## Context

`ROADMAP.md` Sprint 7 states the goal as "o Excel volta a ser possível, mas como convidado, não
como dono" — the spreadsheet comes back as a guest, not as the owner. The tool is the source of
truth; the workbook is a format people already know how to edit.

Two documents describe the export and they disagree. `ALGORITHMS.md §8` (older) specifies two
files, clubs and players, with nested objects flattened to dotted paths. `ROADMAP.md` Sprint 7
(newer, and marked "✔ export com protótipo") specifies one file per workbook tab, using the
original column names, plus a `Paises` tab that never existed in the workbook.

## Decision

### 1. The roadmap's shape wins: one file per tab, with the workbook's own column names

Dotted paths (`kits.home.shirt`) would be a new vocabulary for a user who already has one. The
whole point of writing CSV is that the file opens in the spreadsheet the tool replaced, so the
columns are the columns that spreadsheet has. `ALGORITHMS.md §8` still governs the mechanics — `;`
separator, `|` for lists, `"` quoting with `""` escaping, and exporting as the only gesture that
clears the pending counter.

**Two departures from the original workbook**, both recorded in the generated `Leia-me` tab:

- The tool writes a header row and data rows and nothing else — no title banners, no blank spacer
  rows, no merged sections. A decorated sheet cannot be read back, and the decoration was never
  data.
- `Competicao` is a table with one row per competition rather than the original's `campo;valor`
  column pair, which was an artifact of there being exactly one competition.

### 2. Comma decimals, and a dot is an error

The separator is `;` precisely because pt-BR writes decimals with a comma, so the tool writes
`0,86`. Writing `0.86` would make Excel pt-BR show every number as text, which defeats the only
reason the tool writes CSV.

The reader accepts **only** the comma. A cell spelled `0.86` is rejected by name and line with the
correction spelled out. Accepting both is how a column silently changes meaning halfway down a
file: `0.66` parsed leniently is a club eighty times stronger than it is, and nothing about the
result looks wrong.

### 3. Columns the tool never stored are not emitted

`Audit_Jogadores` had fifteen columns in the workbook and has ten here. The source JSON had
already dropped `anchorAgeApprox`, `generatedFullName`, `reviewedBy` and `reviewDate` before
Sprint 2 ever read it. Emitting them empty would make a lossy round trip look complete. The
`Leia-me` tab names them.

### 4. `Leia-me` is generated; `Calibracao`, `Pesos_Posicao` and `Paises` are read-only

`Leia-me` describes *this* export — its counts, its dialect, and the IP warning that still applies
— rather than round-tripping prose about a spreadsheet that no longer exists. Stored, it would
rot; generated, it cannot.

Calibration is read-only on the way back because it is edited on its own screen, where a change is
shown against everything it moves. A re-fit constant pasted into a spreadsheet silently rewrites
the economy of all 688 players, and a CSV cannot show that.

### 5. Primary and auxiliary tabs, and what a missing row means

A tab that carries an entity's identity — `Competicao`, `Clubes`, `Jogadores`, `GeoNodes`,
`Fontes` — decides which rows **exist**: a row missing from it is a removal. The rest
(`Kits_Estadio`, `Audit_Clubes`, `Audit_Jogadores`) describe part of an entity and can only update
rows that exist in the result; a row for an id that is not there is a stale export and says so.

So adding a club means importing `Clubes`, `Kits_Estadio` and `Audit_Clubes` together. Importing
`Clubes` alone with a new id is refused by name rather than half-built out of defaults — a club
assembled from placeholder colours and a blank audit is exactly the kind of plausible-looking
wrong record this project exists to prevent.

### 6. The diff is computed on the exported representation, not on the domain records

For each tab, the tool renders the current world's rows and the proposed world's rows and compares
them cell by cell. That is the whole trick: the diff can only describe what CSV can express, and
it can never drift from the export, because it **is** the export. A second field catalogue written
for the diff would be a second thing to keep in step with the first.

A consequence worth stating: because the proposed world goes through `WorldDerivations` before the
comparison, editing a kit colour shows the ΔE and luminance the change actually produces, not the
ones the file happened to carry.

### 7. Applying recomputes the plan from the files

`/api/import/apply` does not carry a plan over from the preview; it re-plans from the same files.
A preview left open in a browser tab for an hour therefore cannot write a world that no longer
follows from the files it describes.

### 8. A club that fails to parse stops the players from being read

A broken `Clubes` row drops the club from the result, and then every one of its players looks like
an orphan. Reporting 688 consequences of one cause buries the cause. The clubs have to parse
cleanly before the players are read at all — which costs a second pass in the rare case where both
tabs are broken, and is worth it.

## Consequences

- `CsvRoundTripTests` is the gate: export every importable tab, reimport, and the plan is empty.
  It runs against the real 20-club / 688-player batch, so a column that cannot survive the trip
  fails the build rather than being discovered by a user with a spreadsheet.
- The comparison baseline is the world **as the database holds it** — derived fields recomputed.
  `WorldDerivations.Recalculate(WorldSnapshot)` exists for that: a snapshot read straight from a
  document has not been through the write path, and comparing against one shows changes that are
  not there.
- Removal is a real outcome of an import. The preview names every row that leaves, the count is
  coloured, and the footer says plainly that a row missing from a file is a deletion. That is the
  safeguard; there is no second confirmation, because the preview *is* the confirmation.
- `WorldStore.ReplaceAsync` is the first write path that replaces the whole world. It is
  transactional and only ever reached through the preview.
