# Shape — design brief / wireframe hand-off

**For:** a product/UX design agent, working cold. **Deliverable:** low-fidelity wireframes
(boxes, a heatmap mock, annotations, real placeholder numbers) for the feature below. Function-first
-- **not** a visual-design pass. Read this whole brief before drawing.

Sibling brief, same shape and standards: `D:\work\leopard\docs\pipeline-explorer-design-brief.md`.

---

## 1. The 30-second context (what you're designing inside)

**Leopard** is a desktop app: a *reflection engine for your own data, on your own machine.* A World
of Warcraft raider points it at their combat logs; it parses them locally and reflects the night
back through a few surfaces -- **Ask** (a local model grounded in a pre-computed box score),
**Trends** (recent-window deltas), **Roster** (all-time career per boss), and **Pipeline** (the
engine made legible). Nothing leaves the machine.

The deeper mission -- **the design compass, never the pitch** -- is helping people build intuition
for this chain:

> movement -> events -> logs -> data structures -> visualizations -> insights -> better questions

**Critical tone rule: the instant Shape *feels* like it's teaching, it dies.** The understanding is
a side effect of a surface that's pleasing to look at and poke. Hold this the whole time -- and
Shape is uniquely at risk of two failure modes (a textbook diagram, or a sports-broadcast stat
overlay). See section 7.

---

## 2. What Shape is (the feature)

**The long-exposure signature of a fight.** If Replay is the play-by-play (where each player was at
each moment), Shape is the *photograph taken with the shutter held open* -- all the movement of a
pull collapsed into one image of where the raid actually spent its time, plus a plain contrast of
what a kill looked like versus a wipe.

Two views, one idea ("what was the *shape* of this fight?"):

1. **The heatmap (hero)** -- where the raid stood, overlaid across a pull. Bright = where time was
   spent. This is the long-exposure.
2. **Kill vs Wipe (companion)** -- a small side-by-side: what separated the attempts that won from
   the ones that didn't (deaths, duration, how far you got).

One sentence to design toward: *"Oh -- THAT'S the shape of our wipes."*

Shape is **perspective**, not evidence and not a scoreboard. It answers "what did this fight look
like, in aggregate?" -- not "what happened at 21:03:55" (that's Replay) and not "who topped the
charts" (that's not Leopard at all).

---

## 3. The magic moment -- design everything around this

**The heatmap of the raid's OWN positions blooming out of one of their pulls -- and then the
kill-vs-wipe contrast snapping the "why" into focus.**

The payoff is a non-technical raider looking at a warm smear of color and going *"we kept clumping
in the bottom-left -- that's the room we died in,"* then glancing right and seeing *"our kills
averaged 0 deaths and 1:46; our wipes averaged 2.3 deaths and ended at 31% HP."* Two glances, and
they understand the shape of their own night well enough to ask a sharper question.

If the wireframe nails a generic heatmap but the "this is YOUR pull, these are YOUR positions"
grounding is weak, it has failed. The heatmap is only powerful because it is theirs.

---

## 4. Ground truth -- this is a real engine, not a cartoon

Every value maps to something `Tempo.Projections` already computes. Do **not** invent fields. The
two v1 views come from two real builders (source:
`D:\World of Warcraft\tempo\src\Tempo.Projections\ShapeProjection.cs`, contracts in
`ShapeContracts.cs`):

### Hero: density heatmap -- `TryBuildDensity` -> `ShapeDensityDto`

A spatial occupancy grid for **one pull**. Real fields:

| Field | Meaning |
|---|---|
| `GridW` x `GridH` | heatmap resolution. Engine default **32 x 16** (clamped 8-64 x 4-32). |
| `Cells` | `GridW*GridH` doubles, **normalized 0..1** -- the value to color each cell. |
| `RawCounts` | same length, raw position-sample counts per cell (for tooltips/"N samples here"). |
| `TotalSamples` | total position samples in the pull (the "shutter time"). |
| `MaxBucket` | the busiest cell's raw count -- **drives the top of the color ramp.** |
| `ArenaYd` | the arena extent in yards -- maps the grid to real space (orientation/aspect). |
| `Encounter`, `SyntheticPullId`, `RealPullId` | which boss / which pull this is. |

**This needs replay frames** (position data from advanced combat logging). It is **per-pull** -- you
pick a pull, you see that pull's long-exposure. (See the honesty note in section 8 about
"across attempts.")

### Companion: kill-vs-wipe -- `TryBuildWkDelta` -> `ShapeWkDeltaDto`

Outcome contrast for an encounter. Real fields: `WipeCount`, `KillCount`, and `Rows` -- each a
`ShapeOutcomeRowDto(Label, Unit, Wipe?, Kill?)`. The engine emits exactly these four rows:

| Label | Unit | Note |
|---|---|---|
| Avg deaths | (none) | mean over wipes vs over kills |
| Avg duration | s | mean pull length |
| Peak deaths | (none) | worst single pull |
| Best progress (lower=better) | % | wipe = mean end HP%; kill column is `0` by definition |

`Wipe`/`Kill` are nullable -- a column is null when there are no pulls of that outcome. **No replay
needed**; this is pure outcome math, so it works on any night with at least one wipe and one kill.

### Realistic placeholder numbers (reuse so the brief and app tell one story; *italic* = illustrative)

- Heatmap: *grid 32x16*, **TotalSamples ~4,200**, MaxBucket *96* (one hot cell, bottom-left).
- Kill vs Wipe for *Commander Kroluk (Heroic)* -- **KillCount 1, WipeCount 6**:
  Avg deaths *0 / 2.3* . Avg duration *146s / 52s* . Peak deaths *0 / 4* . Best progress *-- / 31%*.

---

## 5. Frames to wireframe (the actual ask)

Draw these. Annotate generously. Low fidelity.

1. **The heatmap at rest (HERO FRAME)** -- one pull's long-exposure, framed in the arena aspect from
   `ArenaYd`, colored from `Cells` with the ramp topped by `MaxBucket`. Caption it as *their* pull.
   A legend (cool = little time, hot = most time) and a "*4,200 position samples*" line make it read
   as real measured data, not decoration.
2. **The heatmap empty-state** -- when the selected night has **no replay frames**, the hero can't
   render. Don't show a broken box: collapse gracefully to the Kill-vs-Wipe view as the primary,
   with a quiet line ("This night has no movement data -- showing kill vs wipe instead"). This frame
   is load-bearing; thin/older logs will hit it.
3. **The Kill-vs-Wipe contrast panel** -- the four rows, two columns (Kill | Wipe), with the
   counts ("1 kill . 6 wipes") as the header. Make the *difference* legible at a glance (the row
   where wipes and kills diverge most is the story). Resolve: does this sit beside the heatmap, or
   below it?
4. **Selector interplay** -- the **global "Raid night" picker already lives above the tabs** (app
   chrome; do not redraw a per-tab night dropdown). Shape needs a **boss** selector and, for the
   heatmap, a **pull** selector. Show how those two sit within the Shape tab without competing with
   the global picker. (Mirror how Trends places its in-tab "Boss" dropdown.)
5. **First-run / empty** -- before any night is parsed. What invites the first click? (Reuse the
   other tabs' "go to Setup, parse a night" pattern.)
6. **The hand-off** -- Shape must not dead-end. From a shape, how does the user step to **Ask**
   ("ask about this night") or **Trends** ("see this over time")? Show the return to the spine.

Optional if you have appetite:

7. **Cross-attempt overlay (stretch)** -- the *aspiration* is a long-exposure across ALL attempts,
   not one pull. The engine returns per-pull today (section 8). Propose how a multi-pull overlay
   *would* read if/when the data is aggregated -- but mark it clearly as future.

---

## 6. Layout / interaction questions for YOU to resolve (propose, don't over-ask)

- **Heatmap rendering.** Crisp grid cells (honest to the 32x16 data) vs a smoothed/blurred field
  (prettier, less literal)? Pick one and justify. Lean honest unless smoothing clearly wins.
- **Color ramp.** `Cells` is already normalized 0..1, but `MaxBucket`/`TotalSamples` give you the
  real magnitudes for the legend. How does the ramp read for a non-technical eye -- and does it
  survive colorblindness?
- **Arena framing.** `ArenaYd` gives extent/aspect. How do you keep orientation legible without a
  minimap the user has to decode? (No compass-rose lecture.)
- **Pull selection.** Kills and wipes in one list -- how do you let someone flip between "show me a
  wipe" and "show me the kill" fast, since that comparison is half the point?

---

## 7. The central design tension (the hard part)

Shape is positional, which drags it toward **two** wrong places:

1. **Replay.** Replay (the play-by-play, leader/guild lens) is **deliberately parked** -- it's not
   the empowerment core. Shape must NOT become a baby Replay: **no timeline scrubber, no play
   button, no per-frame stepping.** It is one still image (a signature), not a movie.
2. **A stat broadcast.** The Kill-vs-Wipe panel must not curdle into a DPS-meter / leaderboard /
   per-player ranking table. It compares *outcomes* (kill vs wipe), never *players*.

It should feel like holding up a photograph of the night and a short caption -- something you
*look at and understand*, closer to a nature photo than a dashboard. The understanding arrives as a
side effect of looking. If a frame reads as "scrub the replay" or "rank the raiders," redo it.

---

## 8. Scope -- v1 is HEATMAP + KILL/WIPE, nothing else

- **In:** the per-pull density heatmap (hero) and the kill-vs-wipe contrast (companion), the
  empty-state fallback between them, and the hand-off to Ask/Trends.
- **Out (do not design):** the rest of the engine's Shape suite -- **affinity** (player x player
  proximity matrix), **groups** (spatial clustering), **score** (per-player composite), **meter**
  (per-player movement table). And **trend** (time-series) is **excluded** -- the shipped **Trends**
  tab already owns signals-over-time; Shape must not duplicate it.
- **Honesty note (do not paper over):** the engine's `TryBuildDensity` is **per-pull**. The
  "long-exposure *across attempts*" framing is the aspiration; aggregating multiple pulls into one
  overlay is a **v2** step (the grids would need summing, and pulls don't always share arena
  extent). v1 is one pull's long-exposure, with the boss/pull selector making comparison easy. Don't
  imply the overlay exists yet.
- **Later, not v1:** the cross-attempt overlay; the spatial suite (affinity/groups); per-player
  movement.

**Where it'll plug in (for grounding, not for you to build):** when Shape is built it follows the
proven Leopard seam -- a `ShapeArtifact` precomputes density + wkdelta at parse time, caches a
versioned `.shape.v1.json` next to the box score, and a thin `ShapeTab` renders it from `/api/shape`
-- exactly like `BoxScore` / `TrendsArtifact` / `PipelineTrace` do today. Zero Tempo engine changes.

---

## 9. Success condition (how we'll judge the wireframes)

> A non-technical raider opens Shape, looks once, and can say in their own words -- "that's where we
> kept dying" or "that's what our kills had that our wipes didn't" -- and asks a better question
> because of it.

If your wireframes make that outcome feel *inevitable*, they're right.

---

*Source of truth for the engine contracts: `D:\World of Warcraft\tempo\src\Tempo.Projections\ShapeContracts.cs`
and `ShapeProjection.cs`. Product framing: `D:\work\leopard\docs\product-vision.md`. Where Shape sits
in the roadmap: `D:\work\leopard\docs\feature-order.md`. Structural sibling:
`D:\work\leopard\docs\pipeline-explorer-design-brief.md`.*
