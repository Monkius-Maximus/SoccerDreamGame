# ADR-0012 — Competitions as composition: one model, seasons, stages and transition rules

- Status: Accepted (implemented in Sprint 11c; see Clarifications)
- Date: 2026-10-04, clarified 2026-10-04
- Depends on: ADR-0002 (coexistence), ADR-0005 (projection), ADR-0007 (pyramids), ADR-0008
  (undo and pyramid authoring), ADR-0011 (generation)
- Amends: ADR-0005 §5, ADR-0007 §1–§3, ADR-0008 §1, §2 and §4
- Applies to: `src/SoccerSim.Core/World/Competition.cs`, `src/SoccerSim.Core/World/Competitions/`,
  `src/SoccerSim.Core/World/Projection/`, `src/SoccerSim.Core/World/Generation/DivisionGenerator.cs`
  and `DivisionCreation.cs`, `src/SoccerSim.Core/World/Serialization/`,
  `sql/0019_competitions_as_composition.sql`, `tools/SoccerSim.WorldBuilder/`
- Plan: `docs/ROADMAP-GENERATION.md`, Sprint 11c

## Context

The world has two models of the same thing.

- **`Competition`** (migration 0011) is one flat record per edition. It mixes what the
  competition *is* (name, scope, format, promoted and relegated counts) with what one *season* of
  it is (year, edition id, member list).
- **`Division`** (migration 0014) is a country's pyramid level. It mixes the structure (tier,
  format, size, promotion and relegation) with a member list — but it has no season at all.

Both hold members. The pilot league lives only as a `Competition`; a generated division lives only
as a `Division`. The "Ligas" screen therefore showed an empty Série A while the twenty pilot clubs
sat in `cmp_bra_tier1`, and `worldbuilder project` refuses every generated club, because the
projection builds leagues from competitions (ADR-0005 §5) and a division is not one
(ADR-0007 §1). ADR-0011's amendment of 2026-10-03 pinned that refusal and deferred the question
to this ADR.

ADR-0007 §1 was right that the standing structure and the edition are different things. The line
was drawn in the wrong place: between two unrelated records, instead of inside one competition.
Its own argument — "collapsing the two makes *who came up last year* unanswerable" — is not
answered by `Division` either, since a division has no seasons.

The pilot's competition record also stores several numbers the project's rules say must be
derived or must have a target:

| Field | Pilot value | Problem |
| --- | --- | --- |
| `Format` | "Pontos corridos, turno e returno" | Free text, while `Division` uses an enum |
| `Rounds` | 38 | Typed; ADR-0007 §2 says rounds are derived |
| `LeagueTierFloat` | 0.86 | ADR-0005 §4: it *is* the members' mean strength (0.8605) |
| `PrestigeBand` | B2 | D-38: the competition band is derived from participants (RC-03) |
| `ContinentalSlots` | "6 (Continental) + 6 (Continental secundária)" | Free text, no target competition |
| `MemberPredicateId` | `pred_top20_by_national_ranking` | Names a rule nothing executes |

The owner's research (the "lego" model) proposes Competition → Season → Stage → Matchday →
Fixture, with transition rules between stages and competitions. Most of it is the game's runtime.
This ADR takes the part that is authored world data, and draws the line first.

## Decisions

### 1. The tool authors the structure; the game plays it

| The World Builder writes | The game does |
| --- | --- |
| The competition catalogue: identity, scope, country, pyramid level | Fixtures (round-robin pairing) |
| Each competition's stages and their parameters | The calendar and slotting of matchdays |
| Transition rules (promotion, relegation, qualification) | Results, standings, tie-breakers |
| The participants of the **current** season | Applying transition rules at season end |
| Clubs, squads, staff (and staff archetypes, Sprint 12) | Creating the next season |
| | Transfer and market AI, finances at runtime, academy intake |

The tool never writes a fixture, a result, a standing or a next season. Migration 0011's
"Aviso de escopo" stands. The tool's world is one season: the current one (§3).

Matchdays, fixtures, the per-stage participant snapshot and the calendar manager from the research
belong to the game and get their own ADRs on that side when the game needs them.

### 2. One competition model: a definition and its seasons

```
Competition        (CompetitionId, Name, Scope, AnchorGeoNodeId, CountryId?, Level?, ClubCount)
CompetitionStage   (CompetitionId, Ordinal, Kind, Legs)                      — §4
CompetitionSeason  (SeasonId, CompetitionId, Year)
SeasonParticipant  (SeasonId, ClubId, Ordinal)
TransitionRule     (CompetitionId, RankFrom, RankTo, TargetCompetitionId)     — §5
```

- **`Competition`** is the definition. It outlives seasons.
- **`CompetitionSeason`** is one edition. `SeasonId` is the edition id the pilot already uses
  (`edt_bra_tier1_2026`). One season per competition per year (`UNIQUE (CompetitionId, Year)`).
  Its participants keep their authored order, as `CompetitionMembers` did.
- **`CountryId`** is the ISO code. It is required when `Scope` is `SubNational` or `National`,
  and null for zonal, continental and intercontinental scopes. It sits beside `AnchorGeoNodeId`
  because they answer different questions: the anchor is *where on the map*; the country is *whose
  pyramid*. ADR-0007 §4 explains why the two identifiers are not linked, and the pyramid, the
  generator and the country profile all key by ISO.
- **`Level`** is the pyramid level, 1 being the top flight. It is set only on a national league
  that belongs to the pyramid, and null otherwise (a future cup).

`Division` disappears as a type and as a table. "Divisão" stays as the screen's word for a
national league that has a level.

"Who came up last year" is now a question about two seasons of the same competition. The tool
writes only the current one, but the model can hold more when the game hands history back.

### 3. The world has one current season

`CurrentSeason` is a world setting beside the master seed (`WorldMeta`), and the document's `meta`
carries `currentSeason`. It is initialised from the pilot edition: **2026**. Every season the tool
writes is of that year. The tool never advances it, because advancing a season means applying
transition rules, which is the game's job (§1).

### 4. A competition is a sequence of stages; only the league stage exists now

`CompetitionStage.Kind` accepts `League` only, and `Legs` is 1 or 2. A competition has exactly
one stage today (`STAGE_COUNT` refuses more). Knockout and Swiss stages, groups, points per
result, extra time, penalties, tie-breakers and points carried between stages arrive with the
first competition that needs them (a cup or a continental competition), each with its own ADR.

The stage is a table rather than a `Legs` column on `Competition` because that is where the next
stage kind lands without migrating competitions a second time. The code does not build a general
stage engine for one kind: it reads the single stage and refuses anything else.

The `CompetitionFormat` enum is removed. `LeagueSingle` becomes a league stage with one leg, and
`LeagueDouble` one with two legs. `GroupsKnockout`, `KnockoutOnly` and `NationalCup` have no
stage yet. No known database has a division in one of those formats, and migration 0019 aborts if
it finds one (§11).

Rounds and matches stay derived (ADR-0007 §2), now from the stage and `ClubCount`, with the
league rows of that table and the same odd-field rule. The pilot derives 38 rounds and 380
matches, equal to its typed 38, so the typed `Rounds` column goes.

### 5. Promotion, relegation and qualification are transition rules

A rule reads: *at the end of a season of `CompetitionId`, the clubs finishing `RankFrom`..`RankTo`
go to the next season of `TargetCompetitionId`.* Whether that is a promotion, a relegation or a
qualification is derived from the two competitions' levels and scopes, not stored. The tool
authors rules; the game applies them (§1).

Rules replace three fields:

- `PromotedIn` / `RelegatedOut`: counts whose target was implicit ("the division above/below");
- `ContinentalSlots`: free text;
- `MemberPredicateId`: the name of a rule nothing ran. The current season's participants are
  authored; later seasons come from rules.

**The pyramid editor writes rules in pairs.** Adding level *n+1* below level *n* with an exchange
of *k* writes "level *n*, ranks `ClubCount−k+1`..`ClubCount` → level *n+1*" and "level *n+1*,
ranks 1..*k* → level *n*". The owner types one number, so a pyramid built on the screen is
balanced by construction; changing the exchange rewrites both rules.

Checks:

- `TRANSITION_RANGE`: `1 ≤ RankFrom ≤ RankTo ≤ ClubCount` of the source competition.
- `TRANSITION_OVERLAP`: two rules of one competition claim the same rank.
- `TRANSITION_TARGET`: the target exists and is not the source.
- `PYRAMID_FLOW` stays (ADR-0007 §3), computed from rules: per competition, clubs arriving equal
  clubs leaving. It still catches an imbalance the migration carries over from old data (§11).

Play-offs (ranks 3–6 into a play-off stage) need stages and carried points, and wait with §4.

The pilot's continental slots have no target competition in the world, so they cannot become
rules yet. They are recorded here — 6 to the continental competition, 6 to the secondary
continental competition — and become two rules when those competitions are authored.

### 6. The pyramid is a view

`LeaguePyramid` is computed: a country's competitions with a `Level`, ordered by level, each with
its current season. It is not stored. `PyramidRules` keeps its job with renamed codes:

| Before | After |
| --- | --- |
| `TIER_DUP` | `LEVEL_DUP` (also `UNIQUE (CountryId, Level)` in the schema) |
| `TIER_GAP` | `LEVEL_GAP` |
| `CLUB_TWO_DIVISIONS` | `CLUB_TWO_LEAGUES`: a club takes part in at most one levelled season per country per year |
| `DIVISION_SIZE` | `SEASON_UNFILLED` (warning) |
| `FORMAT_UNPLAYABLE` | `STAGE_UNPLAYABLE` |
| `PYRAMID_EMPTY`, `PYRAMID_FLOW` | unchanged |

ADR-0008 §2 (levels enter at the bottom; removing one closes the gap) and §3 (enrolling moves,
deleting refuses) keep their meaning. They now act on competitions with a level and on
current-season participants.

### 7. Derived numbers are derived from the season, not stored

- **League tier float** = the mean `ClubStrength` of the season's participants, rounded to two
  decimals. ADR-0005 §4 already says that is what it is. For the pilot it gives 0.8605 → 0.86,
  the authored value. A season with no participants has no tier float, and projecting it is
  refused.
- **Competition prestige band.** D-38 derives it from the participants by RC-03, which is not in
  the repository. Nothing consumes the band; it is only displayed and exported. It is not stored
  and not shown until RC-03 is ported with its source. When it is, a test pins that RC-03 derives
  the pilot's batch value, **B2** (recorded here so the number is not lost).
- **Rounds and matches**: §4.

Each of the stored copies was a number that could disagree with its own source.

### 8. The projection reads the current season of national leagues

This amends ADR-0005 §5. A legacy `League` is projected for each competition with
`Scope = National` and a `Level`. Its teams are the participants of its current season, the
legacy `Season` is that season's year (1 January – 31 December, as before), and `Tier` comes from
the §7 tier float through ADR-0005 §4's thresholds, which do not change. A club in no projected
league, or in two, is still refused by name.

The legacy tables do not change (ADR-0002). Two things are worth stating so nobody "fixes" them
here:

- Legacy `Tier` is a level of simulation **detail**, not a pyramid level. A Série B whose
  participants average 0.71 projects as `MajorForeign` (2) because of its quality, not because it
  is level 2.
- `Matches` stores both `SeasonId` and `LeagueId`, which is redundant in the legacy schema. That
  is the game's schema and outside this ADR.

After generating a division, `worldbuilder project` succeeds. The refusal test pinned in Sprint 11
becomes a success test.

### 9. Generating a division fills a season

`DivisionGenerator`'s request names a `CompetitionId`: a national league with a level, in the
requested country. Free seats are `ClubCount` minus the participants of its current season, and
generated clubs are appended to those participants. `DivisionCreation` writes clubs, squads and
that season's participants in one transaction, behind one history entry (ADR-0011 §6 unchanged).

The Core types lose the word "Division". The CLI verb `generate-division` and the routes under
`/divisions/` keep it, since that is what a levelled national league is called on the screen;
their id parameter is now a `CompetitionId`.

### 10. The world document carries competitions; the scale snapshot carries countries only

Divisions lived in the `Scale` snapshot (ADR-0008 §1) only because the document had no place for
them. Now they are competitions, so the document carries all of it: `competitions` (definitions,
each with its stages and transition rules), `seasons` (with participants) and
`meta.currentSeason`. `Scale` keeps the countries.

This also fixes a gap nobody had hit yet: exporting a world today loses its generated divisions,
because `Scale` is not part of the export.

- The JSON reader reads the new shape only. A document in the old shape is refused with a message
  that names this ADR and says to re-export from a migrated database.
- The pilot fixture (`TestData/world.json`) is converted once, in the commit that implements this.
- CSV: the `Competicao` tab follows the definition, with new tabs for seasons and transitions.
  The sprint plan names them.
- Undo snapshots taken before 0019 are in the old shape. Migration 0019 deletes the `WorldHistory`
  rows (the stack is capped at 25) and sets `WorldEdits.HistoryId` to null. Migration 0017 already
  says null means "not known to belong to an act", which is the truth after this. The edit trail
  itself is kept.

### 11. Migration 0019 converts what is unambiguous and refuses the rest

It runs in the runner's transaction, so an abort writes nothing.

1. Create the five tables of §2.
2. Set `CurrentSeason` to the largest `Season` of the old competitions (2026 for the pilot). An
   empty database gets it on import.
3. **Each old national competition** becomes a `Competition` with level 1. Its `CountryId` is the
   single country of its members, which is the link ADR-0007 §4 already uses (members in more
   than one country abort). It gets one league
   stage, with two legs when its typed rounds equal the double round-robin of §4 and one leg when
   they equal the single, and its edition becomes a season with the members in order. Level 1 for
   the pilot is authored here: its name says "Primeira Divisão", and the league it mirrors is the
   top flight. A competition of any other scope aborts the migration; none exists, and none can be
   represented before stages beyond the league exist.
4. **Each division** becomes a `Competition` (same id, `Level = Tier`, same name, `ClubCount`, one
   league stage from its format; any other format aborts). Its clubs become the participants of
   its current season, `edt_<divisionId>_<year>`.
5. **A tier-1 division in a country that already got a level-1 competition in step 3** is dropped
   if it is empty, since the pilot league is the authored one. If it has clubs,
   `UNIQUE (CountryId, Level)` aborts the migration. In the owner's database the tier-1 "Série A"
   is empty and is dropped, and the generated Série B is kept at level 2. *(That sentence did not
   come from the owner's database; see Clarification 3.)*
6. **Rules** *(the reading of `PromotedIn` below is reversed; see Clarifications 1–2)* are built from the old divisions' counts by level, after the competitions exist, so
   they attach to whichever competition now holds that level. For tier *t* above tier *t+1*:
   *t*'s `RelegatedOut` gives *t*'s bottom ranks → *t+1*, and *t+1*'s `PromotedIn` gives *t+1*'s
   top ranks → *t*. Counts with no level to point at (the bottom level's, and the pilot's 4 and 4
   when no level 2 exists) are dropped. The two counts stay separate, as they were, and
   `PYRAMID_FLOW` reports any imbalance they carry.
7. Drop `CompetitionMembers`, the old `Competitions`, `DivisionClubs` and `Divisions`. Delete
   `WorldHistory` rows and null `WorldEdits.HistoryId` (§10).

Before applying, the owner checks the "Ligas" tab: a tier-1 division with clubs in a country that
also has an imported league is the one case that aborts.

## Options considered

- **The projection reads the pyramid** (option a in the Sprint 11 report). Rejected: both models
  stay, every reader of "who plays where" asks two places, and the pilot would still need a
  division.
- **Generating a division also writes a `Competition` edition** (option b). Rejected: it invents a
  tier float, an edition id, a predicate and slots at generation time, and leaves two member lists
  that can disagree.
- **Keep both, linked by a foreign key from division to competition.** Rejected: the members are
  still stored twice.
- **Adopt the whole research model now** (knockout, Swiss, groups, tie-breakers, matchdays,
  fixtures). Rejected: fixtures and matchdays are the game's (§1), and stage kinds nobody has data
  for are schema for data nobody has (the reasoning of ADR-0007 §5). The stage table leaves room
  for them.

## What this does not adopt from the research

- **The market value formula** `value ∝ OVR^α` with α = 3. It gives ×1.42 from 80 to 90 OVR,
  while the same text claims about ×10. `WorldEconomy.MarketValueEur` (value doubles every 5.1
  OVR, ×3.9 from 80 to 90) is fitted and sourced, and stays.
- **A 0–10,000 reputation.** It would be a second scale for what the authored `PrestigeBand`
  (D-38) and `ClubStrength` already say. One way.
- **Stadium capacity on the club.** Capacity belongs to the stadium entity, drawn from the
  country index (D-39). That is a separate decision.
- **Academy potential ranges.** At most provisional data, in the academy epic on the game side.
- **Personality archetypes** are adopted, but as data on the staff record in Sprint 12, not here.

## Consequences

- `Division`, `CompetitionFormat`, `CompetitionMembers`, `DivisionClubs` and the flat 16-field
  `Competition` are gone. `LeaguePyramid` is computed. `PyramidEditor` keeps its operations
  (add at the bottom, remove, rewrite, enrol, withdraw), acting on competitions and on
  current-season participants.
- Tests that build pyramids with `PyramidEditor.AddDivision` change. The Sprint 11 test that pins
  the projection's refusal of generated clubs becomes a success test.
- A world exported before 0019 cannot be imported. This is by design: re-export after migrating.
- Undo history taken before 0019 is gone. This is a one-time cost of at most 25 steps.
- ADR-0005 §5, ADR-0007 §1–§3 and ADR-0008 §1, §2 and §4 carry amendment notes. ADR-0011's
  "Projecting a generated division is not part of Sprint 11" is resolved here.
- The game-side work this boundary implies (stages beyond the league, fixtures and calendar,
  applying transitions, finances, academy, market AI) is listed in the roadmap as future epics
  outside the World Builder.

## Clarifications (2026-10-04, Sprint 11c)

Decided by the owner while planning Sprint 11c, where the text above met the code.

1. **§11 step 6 reversed the meaning of `PromotedIn`.** In the code, a division's `PromotedIn` is
   how many clubs come **up into it** from the level below: that is the `Division` comment, the
   `PYRAMID_FLOW` sum of ADR-0007 §3, and the pilot, a top flight with `PromotedIn = 4`. Migration
   0019 follows the code: `RelegatedOut(t)` gives "the bottom ranks of *t* → *t+1*", and
   `PromotedIn(t)` gives "the top ranks of *t+1* → *t*". With the pilot's 4 and 4 at level 1 and a
   level 2 below it, 0019 writes the full pair and the pyramid comes out balanced.
2. **Level 1's counts are the imported league's.** When an empty tier-1 division is dropped next to
   the imported league (§11 step 5), the counts of level 1 are the league's (the pilot's 4 and 4),
   not the dropped division's.
3. **"In the owner's database the tier-1 Série A is empty" came from a test database.** It was
   read off a screenshot of the database Claude Code built to check the Sprint 11 screen, not off
   the owner's `world.db`, which was never inspected. That same test database later had five
   clubs generated into its tier-1 Série A, and 0019 refuses it (step 5). Before applying 0019, the
   owner checks the "Ligas" tab, as this ADR already says.
4. **The geo anchor of a division is stated, not guessed.** A `Competition` needs an
   `AnchorGeoNodeId`, and ADR-0007 §4 does not link an ISO code to a geo node. A level created on
   the screen therefore takes the anchor the author picks, which must be a `Country` node. 0019
   gives each converted division the anchor of its country's national competition, and aborts if
   the country has none.
5. **Only the `Division` record leaves the Core vocabulary.** `DivisionGenerator`,
   `DivisionCreation` and their request and result keep their names, as the roadmap uses them;
   the request's `DivisionId` becomes `CompetitionId`.
6. **0019 refuses four more ambiguous cases**, each with a named message: typed rounds that are
   neither a single nor a double round robin of the club count; a database with clubs or divisions
   but no competition to take the current season from; a national competition with no members to
   take its country from; and two national competitions in one country, which would both be level 1.
7. **`Competitions.CountryId` has no foreign key to `Countries`**, like `Clubs.CountryId`. Undo
   rewrites the countries, and a cascade from there would take the competitions with it. A
   competition in a country with no profile is the audit's to report.
8. **How 0019 handles the foreign keys.** The runner's connection has `foreign_keys = ON`, which
   cannot change inside a transaction, so no table is renamed (SQLite would rewrite the children's
   references, and `RENAME` is not portable). The old rows are copied to staging tables, the old
   tables are dropped children first (`CompetitionMembers` before `Competitions`, `DivisionClubs`
   before `Divisions`), and the new ones are built from the copies. A refusal is a named `CHECK` on
   a guard table, so the owner reads "CHECK constraint failed: 0019 aborted: …" and nothing is written.
9. **Structure is checked once, on every way in.** `CompetitionIntegrity` holds the rules a world's
   competitions must satisfy (a country exactly where the scope needs one, a level only on a
   national league, at least one stage of one or two legs, rule targets that exist, participants
   that are clubs, every season of the current year, every level with its season) and runs on the
   JSON document, the CSV tabs and every undo snapshot. Authoring state (an unbalanced flow, an
   unfilled season) stays a `PyramidRules` finding.
10. **Deleting a club no longer shrinks the competition.** `ClubCount` is now the definition the
    transition rules are written against; the season loses a participant and `SEASON_UNFILLED`
    says so.
11. **CSV.** The tabs are `Competicao` (the definition), `Fases`, `Temporadas` (participants joined by
    `|`) and `Transicoes`. A stage and a rule have no id of their own, so the import diff identifies
    their rows by content, as it does `Fontes`.

