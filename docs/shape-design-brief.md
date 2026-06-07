# Shape — design brief / wireframe hand-off

**For:** a product/UX design agent, working cold. **Deliverable:** low-fidelity wireframes
(boxes, a heatmap mock, annotations, real placeholder numbers) for the feature below. Function-first
-- **not** a visual-design pass. Read this whole brief before drawing.

Sibling brief, same shape and standards: `D:\work\leopard\docs\pipeline-explorer-design-brief.md`.

> **Reviewed 2026-06-06** against the real engine and a real progression log by two passes (a
> verification/V&V lens and a statistical-honesty lens). The corrections from that review are baked
> in below; the load-bearing ones are flagged inline as **[honesty]** and **[V&V]** so you don't
> re-introduce them.

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
a side effect of a surface that's pleasing to look at and poke. Shape is uniquely at risk of two
failure modes (a textbook diagram, or a sports-broadcast stat overlay). See section 7. **And note:
the honesty corrections below must be carried by *design* -- a faintly-rendered single sample reads
as honest without a word of explanation -- not by footnotes that tip into lecturing.**

---

## 2. What Shape is (the feature)

**Perspective on a fight -- two honest views of "what did this look like?"** Shape is not the
play-by-play (that's Replay, deliberately parked) and not a scoreboard. It pairs:

1. **The long-exposure of a pull (hero).** One pull's movement, every position sample over its whole
   duration collapsed into a single image of where the raid stood. A literal long exposure -- of
   *one pull*. **[honesty]** It is **not** "the signature of the fight" in v1: the engine produces
   this per pull, not aggregated across attempts. Reserve the word *signature* for the v2
   cross-attempt overlay (section 8), where aggregation earns it.
2. **Kill vs Wipe across the whole career (companion).** For a boss, what separated the attempts that
   won from the ones that didn't -- deaths, duration, how far you got -- **aggregated across every
   night you pulled it**, not just one night. This is the half that genuinely *is* "across attempts."

So in v1 the "perspective across attempts" the product promises is carried by the **companion**; the
hero is a single-pull view. Say that plainly rather than letting the heatmap imply aggregation it
doesn't do.

One sentence to design toward: *"Oh -- so THAT'S where we kept standing, and that's what our kills
had that our wipes didn't."*

---

## 3. The magic moment -- design everything around this

**The heatmap of the raid's OWN positions blooming out of one pull -- and the kill-vs-wipe contrast,
drawn from every attempt on the boss, sitting beside it.**

The payoff is a non-technical raider looking at a warm-but-honest smear and going *"we kept clumping
bottom-left -- that's the spot we died in,"* then glancing at the contrast: *"our 2 kills of Crown of
the Cosmos averaged X deaths; our 52 wipes averaged Y."* Two glances, and they understand the shape
of the fight well enough to ask a sharper question.

**[honesty]** The contrast shows *what differed*, not *why*. A four-row table of means cannot deliver
a cause (a short low-death kill can happen for reasons the table never measured). Never label it "the
why"; the causal inference is the user's to make.

If the wireframe nails a generic heatmap but the "this is YOUR pull, these are YOUR positions"
grounding is weak, it has failed. The heatmap is only powerful because it is theirs.

---

## 4. Ground truth -- this is a real engine, not a cartoon

Every value maps to something `Tempo.Projections` already computes. Do **not** invent fields. Source:
`D:\World of Warcraft\tempo\src\Tempo.Projections\ShapeProjection.cs`, contracts in `ShapeContracts.cs`.

### Hero: density heatmap -- `TryBuildDensity` -> `ShapeDensityDto`

A spatial occupancy grid for **one pull**:

| Field | Meaning |
|---|---|
| `GridW` x `GridH` | resolution. Engine default **32 x 16 = 512 cells** (clamped 8-64 x 4-32). |
| `Cells` | `GridW*GridH` doubles, **normalized 0..1** -- the value to color each cell (max cell = 1.0). |
| `RawCounts` | raw sample count per cell; `sum(RawCounts) == TotalSamples`. |
| `TotalSamples` | **[honesty]** counts *one alive player per 200ms frame* -- so ~4,200 means roughly **20 players x ~260 moments**, NOT 4,200 independent observations. Over 512 cells that's ~8/cell *on average*, and the raid clumps, so most cells are 0-2 and a few are dozens: the grid is **sparse and lumpy**, not a smooth field. Dead players stop contributing, so short/early-death wipes are sparser still. |
| `MaxBucket` | busiest cell's raw count -- tops the color ramp. |
| `ArenaYd` | **[V&V]** the **per-pull** bounding box of observed positions (auto-fit, padded). Two pulls do **not** share a coordinate frame -- a hot bottom-left cell in pull A is not the same floor tile as in pull B. |

Needs replay frames (position data from advanced combat logging -- **confirmed present** in real logs:
SPELL_DAMAGE carries x,y world coords). It is **per-pull**.

### Companion: kill-vs-wipe -- `TryBuildWkDelta` -> `ShapeWkDeltaDto`, **career-scoped**

Outcome contrast for a boss. Fields: `WipeCount`, `KillCount`, and `Rows` -- each a
`ShapeOutcomeRowDto(Label, Unit, Wipe?, Kill?)`. The engine emits exactly four rows:

| Label | Unit | Note |
|---|---|---|
| Avg deaths | (none) | mean over wipes vs over kills |
| Avg duration | s | mean pull length |
| Peak deaths | (none) | worst single pull |
| Best progress (lower=better) | % | wipe = mean end HP%; **kill column = `0` by definition, not a measured mean** |

**[V&V] Scope is the whole career (across nights), not one night.** `TryBuildWkDelta` resolves via
`CareerProjection`, which contrasts every kill against every wipe of the boss across the corpus. This
is essential: on any single real night a boss is almost always all-kills or all-wipes, so a per-night
wkdelta would have one column empty every time. Career-scoped, it is rich.

`Wipe`/`Kill` are nullable. **[honesty]** When a column has **N=1**, do not write "averaged" -- it's a
single observation. When a column is **entirely null** (boss never killed, or never wiped), do not
draw a two-column contrast with a row of dashes; render the populated side honestly and drop the empty
column (see frame 5.4).

### Real, verified placeholder numbers (use these -- they exist in the corpus; *italic* = illustrative)

- **Crown of the Cosmos [Heroic]** -- **2 kills, 52 wipes** (54 attempts). The flagship wkdelta case.
- **Dimensius, the All-Devouring [Heroic]** -- 8 kills, 43 wipes. (48 careers corpus-wide have both.)
- **Midnight Falls [Heroic]** -- **44 wipes, 0 kills.** The one-sided case (frame 5.4) -- and the best
  heatmap target (44 pulls of real position data).
- Heatmap of one Midnight Falls pull: *grid 32x16*, **TotalSamples ~4,200** (~260 moments), MaxBucket *96*.

---

## 5. Frames to wireframe (the actual ask)

Draw these. Annotate generously. Low fidelity.

1. **Heatmap at rest (HERO FRAME)** -- one pull's long-exposure, in that pull's `ArenaYd` aspect,
   colored from `Cells` topped by `MaxBucket`. Caption it as *their* pull, **labeled self-scaled**
   ("framed to this pull's spread") -- **[V&V]** never imply two pulls share a frame. Legend (cool =
   little time, hot = most), and an honest readings line ("~4,200 position readings across ~260
   moments"). Show a **thin-data caption** when `TotalSamples` is low rather than rendering speckle as
   if it were signal.
2. **Heatmap empty-state (no replay)** -- when the night has no movement data, the hero can't render.
   Don't show a broken box: lead with the career Kill-vs-Wipe view and a quiet line ("This night has
   no movement data -- showing kill vs wipe instead").
3. **The Kill-vs-Wipe contrast (career)** -- four rows, two columns (Kill | Wipe), header = the career
   counts ("2 kills . 52 wipes, all-time"). Make the *biggest-divergence* row the eye's anchor.
4. **[honesty] The one-sided frame (THIS IS THE COMMON CASE)** -- a boss never killed (Midnight Falls,
   44 wipes / 0 kills). Do **not** draw an empty Kill column of dashes. Render the wipe side alone:
   "44 wipes, no kill yet -- here's what the wipes looked like." Design this as first-class, not a
   degenerate fallback.
5. **Selector interplay** -- the **global "Raid night" picker already lives above the tabs** (app
   chrome; do not redraw a per-tab night dropdown). Shape needs a **boss** selector and, for the
   heatmap, a **pull** selector. With ~44 pulls, label each pull by N + end HP% + duration + deaths so
   the list itself reads as a mini-progression -- not 44 identical "Wipe" rows. (Career wkdelta is
   boss-scoped and ignores the night picker; say so.)
6. **First-run / empty** -- before any night is parsed. What invites the first click?
7. **The hand-off** -- Shape must not dead-end. From a shape, how do you step to **Ask** ("ask about
   this night") or **Roster** ("see this boss all-time")?

Optional if you have appetite:

8. **Cross-attempt overlay (stretch / v2)** -- the *aspiration*: a long-exposure across ALL attempts,
   where "signature" becomes earned. The engine returns per-pull today and per-pull arenas differ, so
   this needs real aggregation work. Mark clearly as future.

---

## 6. Layout / interaction questions for YOU to resolve (propose, don't over-ask)

- **[honesty] Heatmap rendering.** Crisp cells (honest to sparse 32x16 buckets) vs smoothing. **Lean
  crisp.** Heavy smoothing/interpolation paints a glossy continuous field over sparse data -- that
  manufactures resolution that isn't there, which is the *less* honest choice, not just the prettier
  one. Light smoothing at most.
- **Color ramp.** `Cells` is normalized 0..1; `MaxBucket`/`TotalSamples` give real magnitudes for the
  legend. Does it read for a non-technical eye, and survive colorblindness?
- **Arena framing.** `ArenaYd` gives per-pull extent/aspect. Keep orientation legible without a
  minimap to decode, and without implying a shared coordinate frame between pulls.
- **Pull selection.** ~44 pulls -- make the selector itself a progression read (see frame 5.5).

---

## 7. The central design tension (the hard part)

Shape is positional + comparative, which drags it toward two wrong places:

1. **Replay.** Replay (play-by-play, leader lens) is **deliberately parked.** Shape must NOT become a
   baby Replay: **no timeline scrubber, no play button, no per-frame stepping.** It is one still image
   (time integrated out), not a movie. Re-introducing the time axis is exactly the line not to cross --
   this is a principled boundary, keep it.
2. **A stat broadcast.** The Kill-vs-Wipe panel must not curdle into a DPS-meter / leaderboard /
   per-player ranking. It compares *outcomes* (kill vs wipe), **never players.**

It should feel like holding up a photograph plus a short, honest caption -- closer to a nature photo
than a dashboard. If a frame reads as "scrub the replay" or "rank the raiders," redo it.

---

## 8. Scope -- v1 is HEATMAP (per-pull) + KILL/WIPE (career), nothing else

- **In:** the per-pull density heatmap (hero) and the career-scoped kill-vs-wipe contrast (companion),
  the no-replay empty-state, the one-sided (all-wipe / all-kill) frame, small-N honesty, and the
  hand-off to Ask/Roster.
- **Out (do not design):** the rest of the engine's Shape suite -- **affinity** (player x player
  proximity), **groups** (clustering), **score** (per-player composite), **meter** (per-player table).
  And **trend** (time-series) is **excluded** -- the shipped **Trends** tab owns signals-over-time.
- **[V&V] Build implication (for grounding, not for you to draw):** density is per-pull and caches
  cleanly as a per-night artifact; **wkdelta is career-scoped, so it cannot be a pure per-night cached
  artifact** -- it's computed from the fanned career inputs (the same fan-in `/api/career` does), live
  or recomputed as nights are added. When built it follows the proven seam (`ShapeArtifact` ->
  `/api/shape` -> thin `ShapeTab`, mirroring `BoxScore`/`TrendsArtifact`/`PipelineTrace`), with
  acceptance tests at **both** the artifact layer and the rendered/pixel layer (prior latent bugs here
  -- a WebView2 cache, an IPv4 proxy -- shipped because only the data layer was checked).
- **Later, not v1:** the cross-attempt overlay (where "signature" is earned); the spatial suite.

---

## 9. Success condition (how we'll judge the wireframes)

> A non-technical raider opens Shape, looks once, and can say in their own words -- "that's where we
> kept standing" or "that's what our kills had that our wipes didn't" -- and asks a better question
> because of it.

If your wireframes make that outcome feel *inevitable* -- and never make a single data point look like
a trend -- they're right.

---

*Source of truth for the engine contracts: `D:\World of Warcraft\tempo\src\Tempo.Projections\ShapeContracts.cs`
and `ShapeProjection.cs`. Product framing: `D:\work\leopard\docs\product-vision.md`. Roadmap:
`D:\work\leopard\docs\feature-order.md`. Structural sibling:
`D:\work\leopard\docs\pipeline-explorer-design-brief.md`.*
