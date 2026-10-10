# ADR-0016 — Decision identifiers: ADRs from now on, prefixes for the legacy registers

- Status: Accepted
- Date: 2026-10-10
- Depends on: ADR-0015 (language)
- Applies to: `docs/adr/`, `docs/legacy/` (new), `docs/decisions/LEGACY-IDS.md` (new), and every
  citation of a decision in code, comments, ADRs and documents

## Context

Before the World Builder, design decisions were recorded in twelve pt-BR "decision registers",
written in separate conversations. Each one numbered its own decisions without seeing the
others. Most of them started at D-30, so the same number means different things depending on
the register:

- `D-30` exists in eight registers with eight meanings.
- `D-38` exists in eight registers too. The *Documento Consolidado* calls the D-38–D-52 range of
  the calibration register globally unique, but it is not.
- `D-01`–`D-04` mean one thing in the World Builder handoff (the host, the schemas, the source of
  truth, the ΔE threshold) and another in the reconstructed ledger (D-01 is "the track is 2D").

The code cites `D-38` and `D-39` in comments, and the ADRs cite `D-01`–`D-04`. A reader cannot
tell which register they come from. Every register also lives outside this repository, in the
owner's Claude project, so a contributor who clones the repository cannot read them at all.

## Decisions

### 1. Every new decision is an ADR

`docs/adr/00NN-*.md`, in the existing format, is the only global decision identifier from now on.
No new `D-NN` is created anywhere.

### 2. Legacy identifiers get a prefix for their register, and are never renumbered

A legacy decision is cited as `<PREFIX>-D<NN>`: `GEO-D30`, `CAL-D38`, `WBH-D03`. The register's
own text is not edited, so a reader holding only the old text still finds the decision. The
prefix says which register to open.

| Prefix | Register | Range |
| --- | --- | --- |
| `CORE` | Ledger canônico (D-01–D-09, reconstructed) | D-01–D-09 |
| `WBH` | World Builder handoff (`ROADMAP.md`) | D-01–D-04 |
| `WORLD` | Registro de Decisões — Camada de Autoria de Mundo | D-10–D-29 |
| `SIM` | Registro de Decisões — SimCore | D-30–D-39 |
| `REF` | Registro de Decisões — Arbitragem | D-30–D-38 |
| `PHYS` | Física Ambiental — Etapa 2 | D-30–D-45 |
| `PLOT` | Registro de Decisões — PlotEditor | D-30–D-49 |
| `APPR` | Registro de Decisões — Pivô 2D do Criador de Personagem | D-30–D-39 |
| `SMR` | Registro de Decisões — Structured Match Resolver (Tier 1.5) | D-30–D-45 |
| `ARCH` | Registro de Decisões — Arquétipos, Coesão e Vida de Carreira | D-30–D-62 |
| `CAL` | Registro de Decisões — Calibração e Autoria de Mundo | D-38–D-52 |
| `GEO` | Registro de Decisões — Geografia e Escopo de Competições | D-30–D-37, D-53–D-57 |

`RC-01`–`RC-07` (Regras e Competições), `T-01`–`T-14` (Criação de Personagem e Ciclo de Vida),
`RND-01`–`06` and `LED-01`–`05` are already unique and keep their names.

### 3. The legacy registers enter the repository frozen

The registers and design documents move into `docs/legacy/` as they are, in pt-BR, and are
read-only. They are history. When one of their decisions changes, the change is a new ADR that
names the legacy id it supersedes. The legacy file stays untouched.

Archived material keeps a folder of its own:
- **VersaoB**, the 3D-hybrid variant in the style of Project Zomboid, goes to
  `docs/legacy/versao-b/` until it gets a repository of its own.
- The 3D migration plan goes to `docs/legacy/archived/`.

### 4. One table maps every legacy id

`docs/decisions/LEGACY-IDS.md` holds the table in §2. Next to each prefix it gives the file in
`docs/legacy/`. The `docs/decisions/` folder of patch F0-A, the reconstructed `CORE` ledger,
lands under the same folder.

### 5. Citations are rewritten once

Every unprefixed `D-NN` in code comments and ADRs is rewritten to its prefixed form in Sprint
11d. The code cites `CAL-D38`/`CAL-D39`; ADR-0001 and the others cite `WBH-D01`…`WBH-D04`. From
then on, an unprefixed `D-NN` in a new commit is a review error.

## Options considered

- **Renumber everything into one global sequence.** Rejected. Every citation in every old
  document would be wrong at once, and nobody could follow an old reference any more.
- **Convert every living decision into an ADR.** Rejected for now. It is close to two hundred
  decisions; prefixes reach the same clarity at a fraction of the cost. A legacy decision becomes
  an ADR when someone changes it.
- **Leave the registers in the Claude project.** Rejected. A contributor who clones the
  repository would still see only half the design.

## Consequences

- The repository becomes the single place where design and code are both readable.
- The design documents stay in pt-BR, inside `docs/legacy/` only. ADR-0015's English rule
  applies to everything new.
- Known divergences between legacy decisions and code are listed in `docs/RENAMES-0020.md`
  (findings section). Fixing them is work for named sprints, not for this ADR:
  - `GEO-D53` (confederation): the code still has it in the geographic tree;
  - `CAL-D39` (stadium capacity): the generator still draws it by band;
  - `displayCode` uniqueness: `WORLD-D20` and the club-identity document disagree.
