# Roadmap

A sprint breakdown of what stands between the project as it is today and a game that can be played
end to end. Written from the engineering side: sprints are ordered by **risk retired**, not by which
feature is most appealing to build.

Kept in English for consistency with the other design docs; player-facing text remains pt-BR.

---

## Where the project actually is

Honest inventory, because the roadmap only makes sense against it.

**Solid.** The engine-agnostic core is genuinely good and carries most of the project's value:
time/calendar with interrupt-and-resume, three-tier simulation LOD, a minute-by-minute match
engine, a tick-based pitch simulation with steering + utility AI, deterministic seeded RNG
throughout, the off-pitch life simulation, SQLite persistence behind ports, and a localisation
contract. **184 test methods, 209 cases, all headless.**

**Thin.** The Godot layer is a design system, four overlays (HUD, phone, quick menu, event modal),
one built-out scene (life-sim), and three stubs. `MatchScene` prints a line and does nothing.

**Unverified.** Everything in `game/` has been compile-checked and never run. There is no CI.

### The two largest unretired risks

1. **Nobody has seen this run.** Every UI decision in the repo is a reasoned guess. Layout,
   anchoring, input routing, autoload ordering, the SQLite connection under Godot's lifecycle — all
   unobserved. `dotnet build` succeeding proves the C# is valid, nothing more. The
   assembly-name bug that made the window render black sat in the repo from the first commit and
   was invisible to every check we had.

2. **Nobody knows if `PitchSimulation` is watchable.** It is deterministic, tested, and tactically
   responsive — none of which means it reads as a football match on screen. It is the substrate for
   the match mode, the exhibition mode and the training mode, so if it does not work visually, three
   sprints of plan change shape. **That is why it comes early.**

---

## Sprint 0 — See it run

**Objective.** Make the project observable, and never lose that again.

**Why now.** Everything else is guesswork until this is true. A blank window cost a full session to
diagnose; the next silent runtime failure will cost the same unless we can see and re-check.

**Scope**
- Confirm the `<AssemblyName>` fix boots (Godot Output must print `[GameBootstrap] Core initialised.`).
- Walk every existing screen once and log what is actually broken — layout, anchoring, input. Expect
  a list; treat it as the sprint's real output.
- **Extract the core wiring out of `GameBootstrap` into a plain `CoreComposition` class.** Today the
  composition root is a Godot `Node`, so nothing about it can be tested. A POCO that takes a
  connection and returns the wired services can be smoke-tested headlessly: migrations apply, career
  loads, wellbeing seeds, a day advances, form persists. That single test would have caught more of
  this session's bugs than any other change.
- **CI**: GitHub Actions running `dotnet build` + `dotnet test` on push and PR.

**Done when** the game opens to the hub menu, all four screens have been visited and their defects
written down, the smoke test passes in CI, and CI is green on the branch.

**Risk retired** — "we cannot see what we are building."

---

## Sprint 1 — The shell

**Objective.** Give the game a layer above "one career in progress".

**Why now.** Every mode after this one needs somewhere to live. Building modes before the frame
means retrofitting each of them later.

**Scope**
- `AppSection` state machine (Hub · KickOff · Career · Club · Settings) sitting **above** `GameMode`.
  `GameMode` keeps meaning "what is running inside a career"; `AppSection` means "which part of the
  application you are in". Same fail-fast transition-table discipline as `ModeStateMachine`.
- Hub scene: the mode grid, with a reusable card control (eyebrow · title · meta · badge, gradient
  wash, no bitmap art required).
- Bottom status ribbon — form, league position, budget — reading live data, blank where the data
  does not exist yet rather than faked.
- Online tiles (Ranked Seasons, Pro Clubs) rendered **disabled and honestly labelled**. They are not
  on this roadmap; see Out of scope.

**Done when** the hub is navigable by keyboard and pad, every tile either enters something or is
visibly disabled, and no tile shows a number the simulation cannot answer.

**Risk retired** — "new modes have nowhere to appear."

---

## Sprint 2 — The rendered match

**Objective.** Draw `PitchSimulation` on screen and make a match playable from the hub.

**Why now.** It is the project's largest unknown and the dependency for three of the six modes.
Discovering it early is worth more than discovering it polished.

**Scope**
- `MatchScene` renders the tick simulation: 22 players, the ball, the pitch, a clock and a
  scoreline. Flat shapes first — this sprint is about legibility, not art.
- **Decouple match entry from a persisted fixture.** `MatchEntryGuard` currently requires a real
  `Matches` row that is unplayed and references existing clubs. An exhibition has no row. Introduce
  a synthetic fixture so the same match layer serves career, Kick Off, and later training.
- Kick Off flow: pick two clubs, play, discard the result.
- Speed controls wired to the real `TimeSpeed` (Paused/Normal/Double/Quadruple — the concept's
  1×/2×/3× does not exist in the core).

**Done when** a match can be watched start to finish from the hub without a career, and the same
seed replays identically.

**Risk retired** — "we do not know whether the match simulation is watchable."

**If it is not**, stop and re-plan here rather than building three modes on top of it. The fallback
is the concept's decision-card presentation, with `PitchSimulation` demoted to a resolver.

---

## Sprint 3 — Saves that hold

**Objective.** The game remembers what happened, across more than one career.

**Why now.** The hub's "RESUME · SAVE 1" card is empty until this exists, and several TODOs in the
code are all the same missing concept.

**Scope**
- Relax `Career`'s `CHECK (Id = 1)` singleton; add save metadata (name, created, last played,
  a summary line for the hub card).
- Persist what currently lives only in memory: the human's **current location**, and the
  **`MasterSeed` per career** (today a constant in `GameBootstrap`, with a TODO admitting it).
- `SaveServiceNode` stops being a path-resolver stub and orchestrates create / load / delete.
- Career creation flow: choose role (player or manager), club, and name. The role switch currently
  hides in the quick menu because there was nowhere better.

**Done when** two independent careers can coexist, be resumed from the hub, and reproduce their
world from their own seed.

**Risk retired** — "the game forgets, and only one story can exist."

---

## Sprint 4 — A world worth showing

**Objective.** Make the numbers on screen mean something.

**Why now.** "3rd · Premier Division · 49 pts" against a 6-club seed is theatre. Everything
downstream — transfers, standings, scouting — needs a real population.

**Scope**
- **World generator**: leagues with full divisions, clubs, and squads, from the seeded PRNG so a
  save's world is reproducible. Names generated, never real — the same licensing reason the club
  list is fictional.
- Club identity: `Teams` gains primary/secondary colour and a crest descriptor. **No colour exists
  in the schema today**, which is why the hub cannot tint itself to your club the way the concept
  does.
- Squad OVR as a derived read model (it is an aggregate, not a stored column).
- Replace the dev seed with generation; keep a tiny deterministic fixture for tests.

**Done when** a generated league table is browsable, clubs are visually distinguishable, and the
hub ribbon shows real standings.

**Risk retired** — "the simulation has nothing to simulate."

---

## Sprint 5 — Visual identity

**Objective.** Make it look like a game rather than a tool.

**Why now.** Deliberately *after* the systems. Art applied to a moving target is art done twice.

**Scope**
- A display typeface (condensed, with an italic). This is the single largest visual gain per unit of
  effort and the main reason the current build reads as a prototype.
- Club-coloured theming: the shell tints to the player's club, as the concept does.
- Card treatments, transitions, and the stylised-3D/fixed-camera direction recorded in
  `UI_DESIGN_SYSTEM.md` applied to the life-sim scene.

**Done when** the hub is screenshot-comparable to the concept without any of its data being faked.

**Risk retired** — "it does not read as a product."

---

## Sprint 6 — The manager career

**Objective.** Make the second career role a game, not just a profile.

**Why now.** The life-sim already honours the manager role fully — profile, activities, derived
outputs. What is missing is everything a manager *does*.

**Scope**
- Squad screen (roster, contracts, morale), tactics board (formation + instructions, feeding the
  `TeamTactics` the pitch AI already consumes), transfer market.
- Board confidence and objectives as a first-class state.
- A dedicated staff/manager entity: a manager career currently points `HumanPlayerId` at a player
  row, which works but is a lie the schema should stop telling.

**Done when** a manager career can be played through a season without touching player-career screens.

**Risk retired** — "half the pitch is a promise."

---

## Sprint 7 — The real world

**Objective.** Replace `TestWorldGazetteer` with the City Searcher import.

Fully specified already in [`WORLD_INTEGRATION.md`](WORLD_INTEGRATION.md), including the ordered
merge sequence and the one question to settle first (how much world a save carries). Sequenced last
because the seam is built and holding — this is the sprint that can move without blocking anything.

---

## Technical debt — tracked, not scheduled

Not sprints. Each is a real cost that should be paid inside whichever sprint next touches it.

| Debt | Cost of leaving it | Pay it during |
| --- | --- | --- |
| **Two persistence styles.** Async `IUnitOfWork` + repositories exist, but the composition root uses parallel synchronous services (`SqliteCareerService`, `SqlitePlayerStateService`, `SqliteEconomyService`). Both are reasonable; having both is not. | New code has to guess which to follow. | Sprint 3 |
| **`ComputeTaskYield` returns empty.** The housing/staff multiplier loop (GDD §5) is half-built: items and inventory tables exist, nothing reads them. | A designed progression loop silently does nothing. | Sprint 4 |
| **Godot layer has no test coverage of any kind**, not even a boot smoke test. | Every UI regression is found by a human, or not at all. | Sprint 0 (partially) |
| **`GameBootstrap` is untestable** because it is a `Node`. | The composition root — the most integration-heavy code — is the least verified. | Sprint 0 |
| **Life-sim time is not spent.** `LifeActivity.DurationHours` is authored and displayed but never advances the clock, so a day has unlimited hours. | The core scarcity of a life-sim does not exist. | Sprint 3 |
| **No season rollover.** Nothing ends a season, resets form, or ages players. | A career cannot reach year two. | Sprint 4 |

---

## Out of scope

**Ranked Seasons and Pro Clubs.** Two of the concept's six tiles are online. That is a server,
matchmaking, and netcode — a separate project, not a later sprint. The architecture was built so it
stays possible (engine-agnostic core, persistence behind ports, deterministic simulation), and that
is the correct amount of investment for now. On the hub they are disabled tiles.

**Photoreal presentation.** The open-world mockup is a mood board. The committed direction is
stylised 3D on a fixed camera — see `UI_DESIGN_SYSTEM.md`.

---

## Sequencing rationale

Sprints 0 → 2 are non-negotiable in that order: you cannot evaluate a match you cannot see, and you
cannot put a match anywhere without a shell.

After Sprint 2 the order is genuinely flexible. If the priority is **showing the project to
someone**, do 5 before 4 — a small world in good clothes demos better than a large world in none.
If the priority is **building the game**, do 4 first, because everything after it depends on a real
population.

Sprint 6 is the largest single body of work and the easiest to defer. Sprint 7 can slot in anywhere
once the City Searcher tool is ready.
