# ADR-0008 — Undo covers the whole world, and pyramids are authored from the screen

Status: accepted
Date: 2026-09-12
Supersedes nothing. Completes the items ADR-0007 listed as not yet built.

## Context

ADR-0007 left the pyramid modelled, validated, persisted and displayed — but not creatable. A
country's divisions could only arrive through a seed or a hand-written row, which is not a tool.
Sprint 9 also asked for an undo stack, and that stack landed first (migration 0015).

Closing the second item exposed a hole in the first. The undo snapshot is the world DOCUMENT: the
exported JSON whose round trip is pinned by a test, which is what makes undo correct by
construction rather than by a second mechanism nobody exercises. Countries and divisions
(migration 0014) are not in that document — the pilot batch predates both. So an undo of a pyramid
edit would have restored nothing at all while the button reported success.

## Decisions

### 1. A snapshot is two documents, not one wider one

`WorldHistory` rows gained a `Scale` column (migration 0016) holding countries and divisions as
their own small JSON shape. `Document` stays exactly the export format.

A second column rather than a wrapper object around both, because the property that makes this
safe is that `Document` is the format the exporter writes and the importer reads. Widening it to
carry structure nobody authored would trade a tested inverse for an untested one, to store data
the export format has no column for anyway.

`RecordAsync` captures both and `UndoAsync` restores both, in that order. There is no path that
restores one without the other: an undo that puts back half the world is the failure the pairing
exists to prevent. `PyramidApiTests.EveryScaleWrite_PushesASnapshotFirst` walks all seven
country-and-pyramid write paths, in the same shape as the theory `UndoTests` already ran over the
world's eight.

### 2. Divisions enter at the bottom, and removing one closes the gap

`PyramidEditor` offers four operations and no fifth. A new division always takes tier n+1;
removing one pulls everything below it up. Inserting a level in the middle is not offered.

This is what makes `TIER_DUP` and `TIER_GAP` — two of the seven rules `PyramidRules` checks —
unreachable from the screen, rather than merely reported after the fact. `PyramidTests`
`NoSequenceOfAddsAndRemovals_CanProduceADuplicateOrMissingTier` states it over every intermediate
step of a build-up and a tear-down, not just the ends: a hole that exists for one step is a
pyramid the author can save and walk away from.

The operations that CAN break a rule — the promotion and relegation flow — are reported instead,
because the balanced state is a judgement about a whole pyramid and a half-finished edit is a
normal thing to be in the middle of.

### 3. Enrolling moves; deleting refuses

Enrolling a club into a division removes it from any other division of the same country. A club
plays one division per country, so a move is what enrolling always means; asking the author to
withdraw first would only give them a chance to forget, and `CLUB_TWO_DIVISIONS` would then be an
error the tool itself caused.

Deleting a division with clubs still in it is refused, and so is deleting a country that still has
clubs or divisions — the same rule the geography tree already applies to a node with clubs under
it. Withdrawing is a decision; a cascade would be a side effect.

### 4. A shape that cannot be played is null, not an exception

`Division.Shape` used to call `CompetitionFormats.Shape`, which throws. That was invisible while
every division came from a seed and had a playable field — and it became a 500 the moment a screen
could produce a division mid-description, because the throw happened while the response was being
serialized.

`CompetitionFormats.Unplayable` now states the rule once and returns the reason. `Shape` throws
using it, `Division.Shape` returns null when it applies, and `PyramidRules` builds
`FORMAT_UNPLAYABLE` from the same string. The screen shows a dash and the finding says why.

The floor of two clubs is checked in `PyramidEditor` as well as in the schema
(`sql/0014_world_countries.sql`). Duplicated deliberately: the schema is the guarantee, and the
editor's copy is what turns a constraint violation into a refusal with a sentence a person can act
on.

### 5. The leagues screen is its own tab

The countries card used to sit at the bottom of the geography editor, read-only. Authoring needs
room, so it moved to tab 04 and geography keeps a one-line pointer to it. The pyramid table IS the
editor: what is typed sits in inputs, what is derived sits in plain cells, and the difference needs
no legend.

Every edit answers with the whole country list rather than the row that changed, for the reason
the geography tree already does — adding a division renumbers the tiers below it and enrolling a
club empties a slot somewhere else, so a screen that patched one row would be drawing a pyramid
that no longer exists.

## Consequences

- `CountryDto` gained a `Roster`: every club of the country and the division it plays in, or null.
  One list rather than two, so a screen cannot show a club as both enrolled and available.
- A new country is created with its money and nothing else — no nationality mix. The sweep reports
  the absence, and `SquadGenerator` refuses to populate a country without one. A guessed
  distribution would quietly make everyone Brazilian.
- `Division.Shape` is nullable. A caller that needs the number and cannot proceed without it calls
  `CompetitionFormats.Shape` directly and gets the throw; the nullable property exists for the
  screens, which have to draw a division that is not finished being described.
- Sprint 9 is now complete apart from two items ADR-0007 deferred on purpose and nobody has asked
  for since: the probable XI on the club page, and global search.
