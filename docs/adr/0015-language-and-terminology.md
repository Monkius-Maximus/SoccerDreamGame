# ADR-0015 — Language and terminology: English in the repository, labels in tables, one term per concept

- Status: Accepted (renames implemented in Sprint 11d; string extraction in a later sprint)
- Date: 2026-10-10
- Depends on: ADR-0006 (CSV exchange), ADR-0012 (competitions), ADR-0013 (identifiers),
  ADR-0014 (league strength), ADR-0016 (decision identifiers)
- Amends: `CLAUDE.md` ("Code, comments and ADRs are in English. The UI and user-facing strings
  are in pt-BR."), ADR-0006 §1 (tab and column names), ADR-0013 §3 (geo nodes above the country),
  ADR-0014 §6 (band of a generated club)
- Applies to: every identifier, data key, file, tab and column in the repository;
  `docs/TERMS.csv` (new); `game/` translation files (new); `tools/SoccerSim.WorldBuilder/wwwroot/`
- Plan: `docs/RENAMES-0020.md` (Sprint 11d), then a string-extraction sprint

## Context

The project's names were chosen over months, in separate conversations, and three problems
accumulated.

**Three languages in one codebase.** Identifiers are mostly English, but some enum values,
record members, columns, JSON keys and CSV tabs are Portuguese (`SquadRole.Titular`,
`WorldSource.Tema`, `WorldSources.Fonte`, `"tema"`, `Competicao`, `Kits_Estadio`). A few tool
labels are European Portuguese ("Alcunha", "Mote"). The data packs written in October 2026 mix
both inside one object (`source: { fonte, url, accessed }`).

**One word, several concepts.** The survey of the repository and the design documents found:

- **"tier"** in nine senses: the simulation's level of detail (`SimulationTier`), a league's
  strength (`TierFloat`, `leagueTierFloat`, `leagueTier`, `club_tier`), a pyramid level
  (`Division.Tier`, now `Level`), an event's stakes (`EventTier`), a trait's grade (`TraitTier`),
  a city's size (`settlementTier`), a difficulty ladder, a discarded body state (`conditionTier`)
  and a fragment of ids (`cmp_bra_tier1`).
- **"band"** in six senses, two of them in direct conflict: the *Sistema de Prestígio* research
  (July) defines B1–B6 as classes of **leagues**, while the code stores B1–B6 on the **club** as
  an authored field (D-38, now `CAL-D38`) and uses it for market value, stadium capacity and
  squad size. Separately: a player's quality band, the reserved attribute range 100–120, card
  colours, age bands.
- **"edição"** means a competition's season in the design documents and an *edit* on the World
  Builder's history screen.
- **"Anchored"** means "derived from a real club or player" in `Provenance` and "has a citable
  source" in `SourceSeal`.
- **"Regen"** is borrowed from Football Manager, where it means a youth player generated to
  replace a retired one. Here it means any generated club or player.

**No line between the internal term and what the player reads.** Nothing says whether "Banda
B3" is a design term, a screen label, or both. A glossary written in August 2026 described the
terms without deciding any, and the confusion persisted.

## Decisions

### 1. English for everything that lives in the repository

Code, comments, identifiers, enum values, JSON keys, SQL tables and columns, CSV tab and column
names, file names, ADRs and technical documentation are in English.

Two things are not identifiers and keep their own language:

- **World content.** Club names, city names, mottos and stadium names are data about the world,
  written in the language of the place they describe.
- **Frozen legacy registers.** The pt-BR decision registers written before this ADR move to
  `docs/legacy/` unchanged and read-only (ADR-0016).

Conversation with the owner is in pt-BR. That happens outside the repository.

### 2. Player-facing text goes only through translation tables

Every string the player reads is looked up through a translation key. None is written in code,
and an enum name is never shown.

- **Format: gettext PO**, the format already chosen for dialogue. It supports plurals and
  context (`msgctxt`) to tell apart words with more than one meaning. It also keeps one file per
  locale, which diffs well in git. A second format for menus would be a second path.
- **Source text and order.** The `msgid` is the en-US label from `docs/TERMS.csv`. pt-BR is the
  first locale written and reviewed, and en-US is the second.
- The files live under `game/locale/` when the game gets its first screen with text.

### 3. Internal term and player label are separate fields

`docs/TERMS.csv` is the term base. It has one row per concept, with these columns:

| Column | Meaning |
| --- | --- |
| `TermId` | The canonical English term |
| `Definition` | What it means, in English |
| `Domain` | The area of the game it belongs to |
| `CodeSymbols` | The symbols that implement it |
| `PlayerVisible` | Whether the player ever sees it |
| `LabelPtBr`, `LabelEnUs` | The player-facing labels |
| `ForbiddenSynonyms` | Words that must not be used for it |
| `Status` | `keep`, `rename` or `decide` |

A concept the player never sees has no label. A concept with labels gets its translation entries
from those two columns, and nowhere else.

### 4. One term, one concept

`docs/TERMS.csv` is normative for names. A new term enters it in the same commit that
introduces it, and a renamed term is updated there in the commit that renames it. The decisions
that resolve the conflicts above:

- **Tier** names only the simulation's level of detail (Tier 1, 1.5, 2, 3). The other senses
  are renamed:
  - league strength → `CompetitionStrength`;
  - event stakes → `EventStakes`;
  - trait grade → `TraitGrade`;
  - city size → `SettlementSize`;
  - pyramid position → `Level`.

  `SimulationTier` values become `Full`, `Statistical` and `Aggregate`. The old names
  (`ActiveHuman`, `MajorForeign`, `Minor`) described a league, but the tier has been dynamic per
  match since `GEO-D32`/`GEO-D33`.
- **Band** is retired as a word. Its two meanings become two concepts:
  - **`RatingClass`** is a class *derived* from a continuous value by thresholds. It is never
    authored or stored. A player's class comes from the overall and is shown as a card colour
    (Bronze, Silver, Gold). A club's or a league's class comes from strength and is shown as
    stars, from ½ to 5. Thresholds are calibration data and follow `ARCH-D43`.
  - **`ClubReputation`** is a club's standing and size: history, crowd, market. It is a separate
    axis from strength, so a historic club in a lower division keeps a high reputation. Its six
    values replace B1–B6 one for one: `World`, `Continental`, `National`, `Regional`, `Local`,
    `Modest`. It is authored with a source for an anchored club (`CAL-D38`, `CAL-D51`). For a
    generated club it is derived from strength (ADR-0014 §6, as amended). Leagues have no
    reputation. A competition's derived reputation (RC-03) is deferred until something reads it.
- **Division** is a *role*, not a type: a national competition with a `Level`. The generator and
  command names keep it (`DivisionGenerator`, `generate-division`).
- **Edition** is not used for a competition's season; the term is `CompetitionSeason`. The
  history screen's "Edição" (an edit) becomes "Alteração".
- **Provenance** values become `Anchored` and `Generated`. `SourceSeal.Anchored` becomes
  `Sourced`.

### 5. The World Builder: pt-BR now, strings out of the code next

The World Builder is an authoring tool. Its screens stay in pt-BR. ADR-0001's amendment makes
it a modding layer later, and modders need other languages. So its text leaves the code in a
later sprint, not in 11d:

- `app.js` reads its strings from one file per language (`wwwroot/i18n/pt-BR.json`).
- Core reports findings and field labels as codes and parameters (it already has codes such as
  `COUNTRY_MISSING`), never as finished sentences.

Until then, the renames in `docs/RENAMES-0020.md` change only the strings that name a renamed
concept.

## Options considered

- **pt-BR everywhere.** Rejected. Code, libraries, Godot and the existing identifiers are in
  English, and the project's rule since `WORLD-D27` already put labels in pt-BR only on screen.
- **Keep the mix and maintain a glossary.** Rejected. The August glossary did exactly that, and
  a newcomer still could not tell which "band" or which "tier" a sentence meant.
- **CSV translation files.** Rejected. Godot supports them, but dialogue already uses PO, and CSV
  has no plurals or context.
- **Keep B1–B6 as the internal values of the club's reputation.** Rejected. The codes only mean
  something to whoever remembers the July research. `Continental` explains itself in code, data
  and export alike.

## Consequences

- Migration 0020 rewrites stored enum values, keys and columns together with the ADR-0013 ids:
  `SquadRole`, `Provenance`, `ClubReputation`, `WorldSources` and the confederation-to-continent
  change of `GEO-D53`. It is one data conversion, not two. The list is `docs/RENAMES-0020.md`.
- ADR-0006 §1 kept the workbook's own tab and column names. Those names are now English
  (`Clubs`, `Competitions`, `Readme`…). A spreadsheet saved before 0020 cannot be imported, as
  ADR-0013 already implies.
- ADR-0013 §3: the confederation row (`geo_wrld_conmebol`) becomes a continent row
  (`geo_wrld_<continent slug>`, kind `Continent`). The confederation is a field of the country
  (`Countries.FootballConfederation`), per `GEO-D53`.
- ADR-0014 §6: "band" reads "reputation", and leagues have no band.
- `CLAUDE.md`'s language line is replaced by a pointer to this ADR.
- Sibling projects start their own term bases from this one, so the same concept keeps the same
  English name across projects. Le Grand Strategos is one of them: it shares the world-builder
  mechanic, but not the code.
