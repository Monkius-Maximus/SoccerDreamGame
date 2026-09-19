# ADR-0010 — The navigable edit history

Status: accepted
Date: 2026-09-19

## Context

The tool has kept two records of the past since Sprint 4 and Sprint 9, and showed neither.

`WorldEdits` (migration 0012) is the **provenance trail**: one append-only row per field change,
what it was, what it became, when, and whether it has been exported. Its stated purpose is the
project's premise one step on — "a number without a source does not enter, and an edit without a
record is the same problem one step later". Only its COUNT was ever displayed, in the top bar.

`WorldHistory` (migrations 0015, 0016) is the **undo stack**: 25 labelled complete-world snapshots.
Its only surface was a button that went back exactly one step. The roadmap names the gap precisely:
"Histórico de edições navegável (a pilha do Desfazer é linear e não é exibida)."

## Decisions

### 1. Two records, one screen, told apart

Tab 07 shows both side by side and says which is which, because they are different kinds of thing
and blending them would make both less trustworthy:

- **Voltar a um ponto** — the stack. Travel. Capped at 25, runs one way, and every entry is a
  world you can return to.
- **Registro de edições** — the trail. Evidence. Unbounded, append-only, paged, and never
  rewritten.

The second half of that is a rule, not an accident: **walking the world back does not edit the
record of what was done**. After returning to an earlier point the trail still holds every row —
a trail that erased itself when you changed your mind would not be evidence of anything. A test
pins it.

### 2. An edit knows which act it belonged to

Migration 0017 adds `WorldEdits.HistoryId`, set by every write path from the id `WorldHistory.RecordAsync`
now returns.

Measured before it was written, which is what settled it: every write path produces one edit except
the CSV import, which produces **one per changed cell under one snapshot** — 218 rows in the
verification run. Without the link a single import is several hundred unexplained lines; with it,
one line saying "218 edições" that the rows below belong to.

Correlating by timestamp instead was rejected. Both records are stamped at almost the same instant,
so it is right under one tab and wrong under two — and "almost right" is the exact failure mode a
provenance trail exists to rule out.

No foreign key. The stack is capped and trims, so an edit reliably outlives its snapshot; the id is
a correlation, and "that act is too old to return to" is a normal state the screen states in words.

### 3. Jumping back is one restore, and it is the same world as stepping

`RevertToAsync` restores the target entry directly and discards it and everything newer, instead of
popping N times. Every entry holds a COMPLETE world, so the two land in the same place — and
`JumpingBackIsTheSameWorldAsSteppingBack` asserts it over two databases rather than leaving it as
an argument.

There is no redo, consistent with ADR-0008. The acts above the target are discarded, so the button
carries the count and the confirmation names it: returning four steps says it is throwing four
things away before it does.

### 4. The timeline never ships the documents

`IWorldHistory.ListAsync` returns `WorldHistoryStep` — id, label, time. The pilot world's snapshot
is 700 KB; twenty-five of them would be 17 MB to draw a list of twenty-five lines.

For the same reason of not lying by arithmetic, an act's edit count is computed over the whole
table (`CountByActAsync`), not over the page on screen. Counted over the page it would shrink to
zero as the reader paged away from the act's own rows.

### 5. A defect the screen exposed, fixed

Drawing the trail made visible that the CSV import recorded the changed row's LABEL on both sides
of the arrow: `Elói → Elói`. A row proving an edit happened and saying nothing about what it was.

`WorldChange.Fields` already carried the exact cells that moved, with before and after. The import
now records one entry per changed cell, `csv:Jogadores.height · 176 → 177` — the same shape a hand
edit records. Additions and removals still record the row label, because there the whole row is
the change.

## Consequences

- `IWorldHistory.PushAsync` returns the new entry's id, and every write path threads it into the
  edits it records. A new write path that forgets leaves its edits with a null act, which the
  screen displays as "sem ação associada" rather than hiding.
- The import's pending-edit count is now the number of changed CELLS, not changed rows. It was
  the row count before; no test pinned the number, and the cell count is the one that matches
  what the trail holds.
- **Still open, and visible on this screen:** geography and league edits push a snapshot but record
  no trail row, so they appear in the left column and contribute nothing to the right. They are
  navigable but not documented. Closing that means recording an edit at nine more write paths; it
  is not part of this change and nobody has asked for it.
- Sprint 9's list is now fully built. What the roadmap still lists as having no prototype is the
  crest's central charge, which it calls a request for art rather than code.
