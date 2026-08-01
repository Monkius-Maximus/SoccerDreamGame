# UI Design System

The interface tokens, the rules that govern them, and what changed on the way from the
[Figma Make concept](https://www.figma.com/make/gtv5W8LxaUT0Ek2YZdQlVr/Open-World-Game-UI-UX) to
the shipping Godot build.

The concept was a React + Tailwind + shadcn/ui prototype. **None of that code ports** — the game is
a compiled Godot executable, so the deliverable extracted from it was a *specification*, not source.
This document is that specification. The implementation lives in
[`game/ui/UiTokens.cs`](../game/ui/UiTokens.cs) (values) and
[`game/ui/UiTheme.cs`](../game/ui/UiTheme.cs) (the Godot `Theme` assembled from them).

The theme is built **in code rather than authored as a `.tres`** so it shows up in a diff and can
never silently drift from the tokens.

---

## Art direction: stylised 3D on a fixed camera

The reference is **Project Zomboid**, and it is worth being precise about what that means, because
an earlier framing of this document got it wrong by calling for "2D".

Zomboid is a 3D game. Since build 41 its characters, vehicles and objects are 3D models — many of
them deliberately *thin*, flattened on the Z axis so they read as sprites — rendered into a fixed
isometric camera and textured in a pixel-art idiom. The result reads as 2D and costs a fraction of
what a free-camera 3D game costs, while keeping everything 3D gives you: real lighting, real
occlusion, rotation, and models that can be reused rather than redrawn per facing.

**That is the target.** Not photoreal — the open-world mockup is a mood board, and its fidelity
implies an art budget that would consume the project. And not flat 2D either, because the assets
would then not carry over if the camera ever moves.

Three consequences for the interface, all already reflected in the tokens:

- **The HUD sits over a rendered scene, not over flat colour.** Panels keep an opaque surface rather
  than heavy transparency; the concept's stacked translucent layers would fight a lit 3D scene.
- **A fixed camera means fixed screen regions.** The persistent strip along the top and the prompt
  strip along the bottom are safe to reserve permanently.
- **Nothing in the design system assumes 2D.** Tokens, theme and controls are Godot `Control` nodes
  drawn on a `CanvasLayer`; they compose over a `Node2D` or a `Node3D` root identically.

---

## Language: code in English, game in Portuguese

The concept prototype was in English and the mockups were in Portuguese, which made every screen a
small translation argument. Resolved by a rule rather than by picking a side:

> **Identifiers stay in English. Everything the player reads is a key.**

The simulation never holds a sentence. `LifeActivity` carries `activity.sleep.name`; whether that
draws as "Dormir" or "Sleep" is decided at draw time by
[`ILocalizer`](../src/SoccerSim.Core/Localization/ILocalizer.cs). pt-BR is the default locale and
the source of truth; `en` exists so the code's identifiers have an obvious counterpart.

Keys are built by [`LocKeys`](../src/SoccerSim.Core/Localization/LocKeys.cs), never written as
literals, so a rename is a compile error instead of a silently missing string. A key with no
translation renders **as the key** — `activity.shower.name` on a button is unmistakable in playtest;
a blank button is not.

`LocalizationTests` enumerates every key the simulation can produce and asserts it resolves in every
locale directly, plus that the locales carry identical key sets. That is the failure this contract
exists to prevent.

---

## What carried over unchanged

The concept's art direction was right and is kept: a deep navy ground, condensed uppercase labels,
near-square corners (4px), and a dense broadcast/scouting-graphics feel. The single best structural
idea — a **persistent status strip** that outlives every scene — is kept and promoted into an
autoload, because the game switches between a calendar, a life-sim and a rendered match, and that
is precisely when the human most needs the state to stay put.

---

## Three corrections

### 1. Type scale re-baselined ×1.6

The concept's smallest label was **5px**, inside a **32px** bar, with 8px nav labels and 11px icons.
At 1080p a 5px font is roughly three pixels of glyph — texture, not text — and a 32px row is a
marginal click target.

| Token | Size | Use |
| --- | --- | --- |
| `FontMicro` | **11px** | The floor. Units and axis labels only, never a sentence. |
| `FontSmall` | 13px | Dense table cells, secondary labels. |
| `FontBody` | 15px | Body copy, default control label. |
| `FontSubtitle` | 18px | Panel titles. |
| `FontTitle` | 22px | Screen titles, headline stat values. |
| `FontDisplay` | 28px | The single hero figure on a screen. |

`HudBarHeight` is **52px** (was 32) — enough for an 11px unit under a 22px value without clipping.
`MinTouchTarget` is **44px** and applies to every clickable row.

The cost is honest: fewer panels fit on screen at once. That is the correct trade.

### 2. Chrome is neutral; the accent is reserved

In the concept `#00e676` was simultaneously the brand, every panel border, every panel title, every
panel's pulsing dot, the primary action, every positive stat, the focus ring, `--chart-1`, the
"LIVE" badge and the corner brackets. When everything pulses, nothing signals.

Here the chrome is slate (`Border`, `BorderStrong`, `TextMuted`) and green is reserved for exactly
three meanings:

- a **positive** value,
- **live** data,
- **"this is you"** (the human's own row).

A direct benefit: the red injury/critical state now reads instantly instead of competing with a
green field.

| Token | Value | Meaning |
| --- | --- | --- |
| `Background` | `#060b18` | Window ground. |
| `Surface` | `#0a1422` | Raised panel. |
| `SurfaceRaised` | `#0d1829` | Panel header, inset row. |
| `SurfaceHover` | `#152238` | Hover/selected wash. |
| `Border` | `#1e2c42` | Default border — **slate, never the accent**. |
| `BorderStrong` | `#31455f` | Interactive or focused border. |
| `TextPrimary` | `#e8edf5` | Primary reading colour. |
| `TextMuted` | `#8aa2bd` | Secondary labels and units. |
| `Positive` | `#00e676` | Positive · live · you. |
| `Info` | `#4d9fff` | Informational; the "adequate" band. |
| `Warning` | `#ffa726` | Degrading but not yet harmful. |
| `Danger` | `#ff4d6a` | Harmful: bottomed-out need, injury, failure. |

### 3. Contrast floor

The concept's secondary text (`#5a7a9a` on `#060b18`) measures about **4.4:1** — under the 4.5:1
minimum for small text, and it was used for nearly every secondary label at 8–9px.

`TextMuted` is lightened to `#8aa2bd`, about **7.5:1**. Every semantic colour clears 6:1 on the
background:

| Colour | Contrast on `Background` |
| --- | --- |
| `TextPrimary` | ~15.7:1 |
| `TextMuted` | ~7.5:1 |
| `Positive` | ~11.8:1 |
| `Warning` | ~10.1:1 |
| `Info` | ~7.2:1 |
| `Danger` | ~6.1:1 |

**Colour is never the only carrier of state.** Need gauges render their numeric value beside the
bar; form modifiers are signed (`+2` / `−3`); the critical-need badge names the needs in text.

---

## Type variations

Godot type variations are this system's equivalent of the concept's utility classes. Set
`ThemeTypeVariation` on a `Label` instead of restating colours at the call site.

| Variation | Size | Colour |
| --- | --- | --- |
| `PanelTitle` | `FontSubtitle` | `TextPrimary` |
| `StatValue` | `FontTitle` | `TextPrimary` |
| `StatUnit` | `FontMicro` | `TextMuted` |
| `Muted` | `FontSmall` | `TextMuted` |
| `Danger` | `FontSmall` | `Danger` |
| `Positive` | `FontSmall` | `Positive` |

## Spacing & geometry

`SpaceXs` 4 · `SpaceSm` 8 · `SpaceMd` 12 · `SpaceLg` 16 · `SpaceXl` 24.
`CornerRadius` 4 · `BorderWidth` 1 · `GaugeHeight` 10.

---

## Need gauge colours

`UiTokens.NeedColor(value)` maps a 0–100 gauge onto the band colours using **the simulation's own
thresholds** (`Needs.CriticalThreshold` etc.), so the visual and mechanical bands cannot drift:

| Band | Range | Colour |
| --- | --- | --- |
| Critical | `< 20` | `Danger` |
| Low | `< 40` | `Warning` |
| Adequate | `< 75` | `Info` |
| Good | `≥ 75` | `Positive` |

---

## What was cut, and why

| Concept element | Disposition |
| --- | --- |
| Needs — four different lists across the artifacts | Reconciled into one set of eight. See the table below. |
| Needs as local component state (5 of 6 wired to nothing) | Rebuilt as real simulation in `SoccerSim.Core/LifeSim`; every gauge is now read from `WellbeingState`. |
| Quick actions: Train / **Match** / Rest / Socialize / **Shop** / **Travel** / Media / Home | Generated from `LifeActivityCatalogue` filtered by career role. Match is a mode transition and Shop is an economy screen — dressing them as need actions blurred what the bar does. |
| 1× / 2× / 3× speed control | The core's `TimeSpeed` is Paused / Normal / **Double** / **Quadruple**. The UI follows the core. |
| Hard-coded player card (`OVR 90`, `€ 4,820,000`) diverging from the state | All HUD tiles read live core state. Tiles are only registered for state the core can answer today — an always-blank tile is worse than an absent one. |
| Real-club squad (Modrić, Vinicius Jr, Ancelotti) | Not carried over. Names must be generated; shipping real squads is a licensing problem. |
| `.dark` block overriding the palette with default shadcn greys | Dropped. There is one theme; the ground is already dark. |
| — | **Added:** `EventResolutionDialog`. The concept had no event-resolution screen across all 15 of its routes, despite the interrupt/resume cycle being the spine of the architecture. |

---

## Need reconciliation

Four different need lists existed across the artifacts. They were folded into one set of eight by
four rules — the reasoning lives in
[`NeedKind`](../src/SoccerSim.Core/LifeSim/NeedKind.cs).

| Source | Listed |
| --- | --- |
| First life-sim implementation | Energy, Nutrition, Fitness, Morale, Social, Focus |
| Open-world HUD mockup | Energia, Fome, Moral, Social, Higiene |
| Phone health app mockup | Energia, Fome, Higiene, Sono, Estresse, Dor Muscular |
| Reimagined prototype (`WELLBEING`) | Energy, Sharpness, Morale, Injury Risk |

**The rules:**

1. **Every gauge points the same way — 100 is always good.** "Fome" and "Dor Muscular" are deficits
   that fill as things worsen. Modelled as `Nutrition` and `MuscleCondition` so no gauge has
   inverted polarity. Worth more than matching a mockup's noun.
2. **"Sono" folds into `Energy`.** Both are answered by the same act and would move together
   forever. Two gauges the player can never separate teach a decision that does not exist.
3. **"Estresse" and "Injury Risk" are not needs.** You cannot rest a stress bar; you rest the things
   that cause it. Both are computed in `WellbeingSnapshot` from several needs at once.
4. **"Sharpness" is `Focus`.** Same concept, different word.

**The result — eight needs:**

| Need | pt-BR | Note |
| --- | --- | --- |
| `Energy` | Energia | Absorbs "Sono". |
| `Nutrition` | Nutrição | "Fome" inverted. |
| `Hygiene` | Higiene | New. Cheap to fix, easy to skip — real pressure on a packed week. |
| `Fitness` | Preparo Físico | |
| `MuscleCondition` | Condição Muscular | New. "Dor Muscular" inverted, and the point of the whole exercise (below). |
| `Morale` | Moral | |
| `Social` | Social | |
| `Focus` | Foco | Was "Sharpness". |

Derived, not gauges: `InjuryRisk`, `Stress`, `DecisionQuality`, `FormModifier`,
`EventProbabilityMultiplier`.

**Why `MuscleCondition` earns its place** rather than being flavour on `Energy`: a player can be
well rested and still sore, and it is soreness — not tiredness — that turns a heavy training week
into a torn hamstring. Training now buys conditioning with rest, food, **soreness and hygiene**, and
soreness is the second-largest input to injury risk. That is a loop the six-need version did not
have.

`Hygiene` needed an activity to service it (`shower`), which surfaced a general rule now pinned by
test: **every need must be raisable by some activity, for both career roles.** A gauge the player
cannot service is not a system, it is a scold.

---

## Contextual input prompts

Adapted from the reimagined prototype's platform map, which had the right abstraction. A screen
declares **verbs**, never buttons:

```
[ (Confirm, "prompt.select"), (Menu, "prompt.formation"), (Back, "prompt.exit") ]
```

[`GameInput`](../game/ui/Input/GameInput.cs) turns a verb into the glyph for whichever device the
human last touched — keyboard, Xbox, or PlayStation (whose shapes are also colour-coded). No screen
ever mentions "Esc" or "Start", so adding a device is a change in one file.

`Esc` and `Start` both map to `Menu`, and `Menu` also means "close whatever is open" — so one key
steps back out of a phone app, then closes the phone, then opens the quick menu, which is the
behaviour players already expect.

Actions are registered into `InputMap` **in code**, not authored in `project.godot`: the editor's
serialised `InputEvent` blobs are unreviewable in a diff and easy to corrupt by hand.

## The in-game phone

Borrowed from The Sims, and it earns its place for a specific reason: it gives the game one
diegetic surface for everything that is *not* the world, so the human never leaves the fiction to
check their schedule. It also gives the interface somewhere to grow — a new system becomes a new
app, not another tab bolted onto a HUD.

Opens on `P` / `Y` / `△`. Three apps today: **Saúde** (the same `NeedsPanel` the life-sim scene
uses, bound to the same service), **Agenda** (date, career, current location), **Mapa** (venues and
travel).

One rule, and it is the one that matters: **every app is a view onto a system that already
exists.** The phone must never become the place where state secretly lives.

---

## Open items

- **Match presentation is undecided.** The concept's match screen is decision cards over an SVG
  pitch; the core already has `PitchSimulation` (~22 steering + utility brains at 60 Hz). These are
  different games and the choice should be deliberate, not settled by whichever screen gets built
  first.
- **The world is a placeholder.** `TestWorldGazetteer` is scaffolding; the real world comes from the
  City Searcher tool in its own sprint. See [`WORLD_INTEGRATION.md`](WORLD_INTEGRATION.md) for the
  seam that was built to receive it and the ordered merge sequence.
- **The quick menu itself is not built.** `Esc`/`Start` currently only dismisses the phone. The verb
  and the prompt exist; the menu it should open does not.
- **HUD tiles** for league position, balance and season objectives need a read model per screen
  (`GameplayViewModel`, `SeasonViewModel`) projecting `SoccerSim.Core` onto the UI. Note the
  concept's `GameContext` used 6 FIFA-style 0–99 attributes while `Players` stores 7 on a 1–20
  `CHECK` — the core's model is the authority; the concept's is a view.
- **Manager path** screens (squad, tactics board, transfers) are not built. The life-sim beneath
  them already supports a manager career, so they extend rather than fork.
