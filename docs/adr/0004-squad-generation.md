# ADR-0004: Squad generation — seeds, measured profiles, and the Regen guarantee

- Status: Accepted
- Date: 2026-09-10
- Depends on: ADR-0003 (what the schema constrains)
- Applies to: `src/SoccerSim.Core/World/Generation/`, `sql/0013_world_generation.sql`,
  `tools/SoccerSim.WorldBuilder/Api/GenerationEndpoints.cs`

## Context

A club without a squad is a dead end: the tool cannot rate it, value it or field it, and the
competition table has nothing to sort it by. `ROADMAP.md` Sprint 5 therefore makes generation part
of authoring a club rather than a separate utility. `ALGORITHMS.md §6` specifies the algorithm; the
decisions below are the ones the specification leaves to the implementation, and each of them can
be got wrong in a way that looks like it works.

## Decision

### 1. The generator does not reproduce the prototype's players

The prototype used mulberry32, because it ran in a browser. This uses
`DeterministicRng.CreateStream` over `SplitMix64Random`, which the repository already requires of
all simulation randomness (`ALGORITHMS.md §6`, RNG warning). The contract is **"the same seed
produces the same squad within this implementation"** — not parity with a browser PRNG.
Reimplementing mulberry32 in C# would trade a reproducible cross-platform generator for a cosmetic
match against a throwaway.

Two consequences worth stating plainly:

- Stream keys are derived with an **FNV-1a hash of the club id**, not `string.GetHashCode()`, which
  is randomised per process and would silently produce a different squad on every run — the exact
  failure a determinism test is meant to catch, arriving as a flaky test rather than a bug.
- The stream is keyed on `(masterSeed, clubId, "squad", seed)`, so regenerating one club cannot
  disturb another club's numbers, and the same pair always lands on the same stream.

### 2. Gaussian, uniform pick and weighted choice live on `IDeterministicRandom`

They are **default interface methods**, not helpers inside the generator. The roadmap asks for this
explicitly, and it is right: the next generator (staff, competitions, calendars) needs the same
three primitives, and a private copy in each is how two callers end up sampling differently. Box-
Muller here deliberately discards the spare normal rather than caching it — a cached value is
hidden state that would make a stream's output depend on how many gaussians were drawn before it.

Existing test doubles implement only the narrower `IRandom`, so nothing had to change to absorb
this.

### 3. The measured profiles are data, not constants

`gen_profiles.json` — the per-position attribute offsets and standard deviations, and the two name
pools — is imported into `GenerationAttributeProfiles` and `GenerationNames`, by its own command
(`worldbuilder import-profiles`) separate from `worldbuilder import`. Two reasons:

- They are a **measurement of the 688 real players**, and re-measuring from a larger batch must not
  require a rebuild, nor silently discard the world it was measured against.
- Importing a world must not clobber them, and vice versa. They are two different acts.

With no profiles loaded, generation **throws** and the API answers 400 naming the remedy. It does
not fall back to invented defaults: a squad sampled from made-up shapes looks exactly like a squad
sampled from measured ones, which is precisely why the failure has to be loud.

### 4. The world's master seed is stored world data

`WorldSettings` holds `masterSeed`, `schemaVersion` and `sourceFile`, written by the importer from
the document's `meta` block. Generation reads the master seed from there rather than taking it as a
parameter, so "the same world, the same club, the same seed" is reproducible across machines and
sessions — including after the database is copied elsewhere.

### 5. Every generated player is `Regen`, with no anchor

`Provenance.Regen`, `Audit.AnchorPlayerName = null`, `AnchorFactsVerified = false`. A generated
player therefore asserts nothing about any real person, which is the entire reason the generator is
allowed to invent freely. `SquadGeneratorTests.EveryGeneratedPlayerIsRegenWithNoAnchor` covers it,
and it is a **safety test, not a style test**: if it starts failing, the fix is never to update the
expectation.

`DeviationMethod` carries the sentinel `"N/A — Regen (sem âncora)"` that `ALGORITHMS.md §6.10`
suggests, where the imported batch leaves the column null (ADR-0003, deviation 3). Both load; the
column stays nullable.

### 6. Preview and write are two endpoints, and generation replaces

`POST /squad/preview` writes nothing and returns the squad, its metrics, the drawn XI and the
composition. `POST /squad/generate` writes. The preview's promise is that the same options produce
the same players, so what the user looked at is what gets stored — which holds because generation
is pure and because the preview applies `WorldDerivations.Recalculate`, the same derivation the
repository applies on write.

Generating **replaces** the club's squad rather than merging into it, in one transaction, and costs
one line in the edit log. Merging has no defensible rule (which of two 30-man squads wins?), and
the destructive path is the one users actually want: regenerate until it looks right. The panel
carries the cost in the preview — how many players, and how many of them are anchored to real
people — and confirms before writing.

## Consequences

- Squad **numbers will not match any prototype screenshot**. Do not treat that as a bug, and do not
  "fix" it by porting mulberry32.
- A fresh database can hold a world but no profiles. That is a legible state — generation says so —
  but it means `worldbuilder import` alone is not a complete setup. Both commands appear in
  `worldbuilder help`.
- `SquadMetrics` gained `AverageOverall` (the whole-squad mean) alongside `Overall` (the eleven
  best). The generator panel shows both because depth is exactly what they differ on; raising the
  squad size moves one and leaves the other alone.
- Generation is the first write path that is not a field patch. It bumps no `RowVersion` — the
  squad is not a column of the club — so it relies on the transaction, not on optimistic
  concurrency. Two users generating for the same club at the same moment is not a case the tool
  handles, and does not need to be until the tool is shared.
