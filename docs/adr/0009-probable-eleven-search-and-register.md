# ADR-0009 — The probable XI, the global search, and the register

Status: accepted
Date: 2026-09-17
Completes the last three items ROADMAP.md Sprint 9 listed. Nothing of that sprint is now unbuilt.

## Context

ADR-0008 closed the multi-league authoring and the undo gap, and named two items as still
deferred: the probable XI on the club page and the global search. A third was outstanding without
having been named — the roadmap asks for **two surfaces over the same league data**, "Pirâmide"
and "Registro", and only the first was built.

All three exist for the same reason: the tool outgrew the screen it started as. A club page can
show a squad and a set of averages and still not answer whether the club can field a side; a rail
that filters clubs is useless when what you remember is half a surname; and a pyramid read one
country at a time cannot show that two countries disagree about what a second division is.

## Decisions

### 1. The XI is derived, and the point of it is the mark

`ProbableEleven` fills the formation from the squad in three passes: naturals, then players whose
secondary positions cover the slot, then whoever is left. Each slot carries why its player is
there — `Natural`, `Secondary`, `Improvised` — and the screen colours the left border by it.

Three passes rather than one greedy sweep, because a single pass in slot order lets an early slot
take the one player a later slot could have filled naturally, and then reports an improvisation
the squad did not actually have. `AVersatileDefenderIsNotSpentOnASlotThatHadANaturalAnyway` is
that case, written down.

Measured over the real batch first, as always: **all twenty clubs field a fully natural eleven** —
no secondary, no improvisation, no empty slot. The feature therefore shows nothing until an edit
breaks something, which is exactly right for an authoring tool: it is a check, not a decoration.
Deleting a club's keepers puts an attacking midfielder in goal in red, with a sentence saying why.

The drawn XI's overall also tracks the independent least-squares fit of ALGORITHMS §6.4
(`XI ≈ 39.6 + 41.35 × clubStrength`) to within 5 across the batch, mean −1.3. That cross-check is
pinned at a band of 6: if the selection ever starts picking badly, a number derived somewhere else
entirely is what notices.

It is not stored, and it is not editable. It is a function of the squad and the tactical style,
both of which the same page edits — a stored XI would be wrong one edit later — so it rides on the
club page response rather than a route of its own.

### 2. Search normalises the query, never the answer

Matching happens on a lower-cased, accent-stripped copy; what comes back is always the original
text. The batch is Portuguese and nobody types "Pó de Arroz" with the accent when looking for it.
Accents are stripped by Unicode decomposition rather than a table of letter pairs, because a table
is a list of the accents somebody happened to think of.

All seven categories the roadmap lists are swept, and each hit says which screen it opens — that is
a property of what the hit IS, so it is decided in Core rather than guessed at by the front end. A
player hit additionally carries the club to open, because opening a player means opening the club
page they live on.

Two limits, both deliberate. Below two characters the answer is "most of the world", so the answer
is nothing. Per category the screen gets at most twelve, but the group reports its true count: "rio"
matches 154 players, and a page handed all of them would have one answer on it.

Fields are searched wider than they are shown. "Carioca" returns four clubs, not three, because
`Pó de Arroz Carioca` is the official name of the fourth — and hiding it would hide exactly the club
whose short name the author could not remember.

### 3. The register is a surface, not a summary

`/api/register` returns every division and every competition of every country, flat. Divisions and
competitions appear side by side, each labelled with its kind, rather than flattened into one:
a `Division` is the standing structure and a `Competition` is a frozen edition of one
(docs/adr/0008), and merging them would be the tool asserting they are the same thing.

It shares the Ligas tab with the pyramid behind a switch, and both surfaces load together — a
toggle that has to fetch is a toggle that flickers.

## Consequences

- `ClubPageDto` gained `Eleven`. The club page response is now the squad, its metrics, its
  invariants and the side it would field, which is what "a club is one page, so it is also one
  request" has meant since ADR-0001.
- `CountryProfiles.NamesFrom` moved into Core. The walk from a club's geo node up to its Country
  was duplicated across two endpoint files; it is a fact about how the ISO code relates to the
  tree, so it belongs with the country code, not with an HTTP handler.
- Search is the first screen that reads on every keystroke. Everything else in this tool commits on
  change, because an edit is a decision — a query is not, it is a question being narrowed, and a
  stale response is discarded rather than allowed to overwrite a later one's answer.
- Sprint 9 is complete. What remains unbuilt in the roadmap is listed there as having no prototype:
  a navigable edit history (the undo stack is linear and not displayed), and the crest's central
  charge, which the roadmap itself calls a request for art rather than code.
