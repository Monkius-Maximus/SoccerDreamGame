# World Integration — the City Searcher merge plan

The real world is authored in the **City Searcher** editor and lands in its own sprint. This
document records the seam that was built to receive it, so the merge is an import rather than a
rewrite.

Until then the game runs on
[`TestWorldGazetteer`](../src/SoccerSim.Core/World/TestWorldGazetteer.cs) — a fictional city
(Verith) with eleven nodes, which exists only so the systems that need somewhere to be could be
built and tested.

---

## Why a test world at all

Three systems needed a world before the world existed: venue-gated activities, travel, and the
phone's map app. Building them against a placeholder is fine **provided the placeholder has the
same shape as the real thing** — otherwise the import becomes a refactor of everything that touched
it.

So the test world is not an arbitrary fixture. It copies the tool's conventions exactly.

## The four things that make the merge ordered

### 1. Same id scheme

City Searcher emits a dotted path down to the city, then `#` and a flat slug for anything inside it:

```
br.sudeste.rj.rio-de-janeiro#lapa
test.verith#apartamento
```

Ids are **stable and are what localisation keys are built from** (`location.<id>.name`), so
importing a real world does not invalidate a translation or a save. `WorldTests` pins the scheme.

### 2. Same hierarchy levels

[`LocationKind`](../src/SoccerSim.Core/World/WorldLocation.cs) mirrors the tool's levels
one-for-one: `Region → State → Subregion → City → District → Venue`.

### 3. A port, not a class

Everything reaches the world through
[`IWorldGazetteer`](../src/SoccerSim.Core/World/IWorldGazetteer.cs). Nothing anywhere names a
specific place. **Swapping the implementation is the entire integration.**

### 4. Categories, not per-place authoring

The join between world and life-sim is `VenueCategory`. A `LifeActivity` declares the *kind* of
venue it needs (`physio` needs `Medical`), and any venue of that category can host it. Mapping the
gazetteer's free-text `detail.type` onto a category is therefore the whole content step — activities
are never re-authored per location.

---

## What the gazetteer carries that this port does not (yet)

City Searcher attaches richer data than the life-sim consumes. Deliberately left out of
`WorldLocation`, with where each belongs:

| Gazetteer field | Example | Destination |
| --- | --- | --- |
| `detail.actions[]` with `cadence`/`kind` | "Tuesday event night" (`weekly`, `event`); "wish-ribbon quest" (`quest`) | **Events and quests**, not need activities. These are flavour bound to a place and belong on the `EventManager` seam — a location-scoped `EventDefinition` source. |
| `lens` overlays | `brasileirao-serie-a-2026` → clubs, stadiums, derbies per city | **Competition binding.** This is the bridge from world to football sim: it says which clubs a city contains. Feeds league/team generation. |
| `archetypeId` + inheritance | `sertao-nordestino`, `metropolitana-nordestina-litoral` | **Procedural flavour.** An archetype can supply default venues and event weights to every city that inherits it — the cheapest way to make a large world feel authored. |
| `layout` (x/y/w/h) | Per-node rectangles, editable | **Map rendering and routed travel.** Replaces the current flat `TravelMinutes`. |
| `tier`, `signature`, `twinCity` | `hero`, `generic+signature` | **Level-of-detail for places** — mirrors the simulation's existing LOD tiers, and should probably reuse them. |

None of these require reshaping `WorldLocation`; each is an additive seam.

---

## Merge sequence

Ordered so nothing is built twice:

1. **Export the gazetteer to a data file.** JSON is what the tool already emits. Land it as an
   embedded resource beside the SQL migrations.
2. **Write `GazetteerImporter`** in `SoccerSim.Core/World` — JSON → `WorldLocation[]`, including the
   `detail.type` → `VenueCategory` mapping table. This is where the one genuinely fiddly piece of
   work lives, and it is isolated to one class.
3. **Add `ImportedWorldGazetteer : IWorldGazetteer`.** Everything downstream keeps working
   untouched; `WorldTests`' shape assertions should pass against it unchanged, which is the signal
   that the seam held.
4. **Generate the location string keys.** Names come from the gazetteer, so the importer should emit
   catalogue entries rather than having them hand-written — at which point the catalogue is likely
   ready to move from C# to `.po` files.
5. **Swap the composition root.** One line in `GameBootstrap`.
6. **Delete `TestWorldGazetteer` and its strings.** It is scaffolding; leaving it around invites
   someone to test against it forever.
7. **Then** the additive seams above, in whatever order the game needs them — `lens` first if club
   generation is the priority, `layout` first if the map screen is.

Steps 1–6 do not touch the life simulation, the UI, or the save schema.

---

## Open question to settle before step 1

**How much world does a save carry?** The gazetteer is world-scoped and static; a save is
career-scoped. The current schema keys wellbeing and the activity log by career and stores no
location at all — the human's position lives in memory only.

Before the import, decide whether a save persists its current location (almost certainly yes, one
`TEXT` column on `Career`) and whether per-location state — a venue the player has unlocked, a
property they own — is career state or world state. That decision shapes the migration, and it is
cheaper to make it now than after a world with thousands of nodes exists.
