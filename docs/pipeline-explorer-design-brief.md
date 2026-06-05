# Pipeline Explorer — design brief / wireframe hand-off

**For:** a product/UX design agent, working cold. **Deliverable:** low-fidelity wireframes
(boxes, arrows, annotations, real placeholder numbers) for the feature below. Function-first —
**not** a visual-design pass. Read this whole brief before drawing.

---

## 1. The 30-second context (what you're designing inside)

**Leopard** is a desktop app: a *reflection engine for your own data, on your own machine.* A World
of Warcraft raider points it at their combat logs; it parses them locally and lets them **Ask**
questions of a night — answered by a **local** AI model, **grounded** in the real events (it restates
a pre-computed box score, never makes numbers up). Nothing leaves the machine.

The deeper mission — **the design compass, never the pitch** — is helping people build intuition for
this chain:

> movement → events → logs → data structures → visualizations → insights → better questions

**Critical tone rule: the instant Leopard *feels* like it's teaching, it dies.** The teaching is a
side effect of a tool that's delightful to poke. You are designing the surface most at risk of
violating this — see §7. Hold it the whole time.

---

## 2. What the Pipeline Explorer is (the feature)

**The engine made visible.** A clickable dataflow map of how a raw multi-gigabyte combat log becomes
the small numbers and pictures Leopard shows. It turns the chain above into something you *explore*.

One sentence: *"Oh — THAT'S how my 2-million-line log becomes a single coordination number."*

It is the flagship — the purest expression of the mission. It earns its place because, in a demo,
data-literate people (finance, data-science, engineering folks) recognized Leopard's other surfaces
instantly *because they could see the move being made.* The Pipeline Explorer makes that move legible
to **anyone**.

---

## 3. The magic moment — design everything around this

**Clicking a node and seeing the funnel happen to YOUR data, with real numbers and a real sample.**

The graph topology (boxes + arrows) is just the frame. The product is the **drill-in**: each node,
when opened, shows — for the night the user actually parsed — what it received, a concrete sample of
it, and what it produced. The payoff is the moment a non-technical raider watches *2,140,000 lines →
847,000 events → 200 kept → one number (0.73)* and gets it.

If the wireframe nails the at-rest map but the drill-in is an afterthought, it has failed. **The
drill-in is the hero.**

---

## 4. Ground truth — this is a real engine, not a cartoon

Every node maps to a transform that actually exists. Do **not** invent stages. Here is the real
shape (a linear spine that fans out at the end):

```
Raw log ─► Lex ─► Classify ─► Segment (pulls) ─► Trim (~200 key events) ─┬─► the Rack:
                                                                         │   Followership / Percolation / Flex
                                                                         ├─► Shape   (long-exposure signature)
                                                                         ├─► Trends  (signals over time)
                                                                         └─► Box score ─► Ask
```

- **Lex** — read the raw text into tokenized lines.
- **Classify** — label each line as a combat event (or discard non-combat/system noise).
- **Segment** — group events into *pulls* (individual boss attempts).
- **Trim** — keep only the ~200 events that matter for a pull; drop the rest. *(This is the dramatic
  collapse — feature it.)*
- **The Rack** — a *catalog of pluggable analyzer modules* (Followership, Percolation, Flex-meter, …).
  Each is a clickable node. Think "swappable attachments," not a fixed circuit.
- **Shape / Trends** — projections that turn the kept events into a signature / a time series.
- **Box score** — the compact pre-computed summary that **Ask** reads.

**Realistic per-stage numbers for one night** (reuse these as placeholders so the brief and the app
tell the same story; the *italic* ones are illustrative):

| Stage | What flows through |
|---|---|
| Raw log | **2,140,000** lines |
| Lex | 2,140,000 lines tokenized *(a few malformed dropped)* |
| Classify | **847,000** combat events *(the rest was non-combat noise)* |
| Segment | *14 pulls* — *3 kills, 11 wipes* |
| Trim | **~200** key events **per pull** |
| Followership (Rack) | **0.73**, computed from *those 200 events* |

**A worked drill-in (the Trim node) — wireframe the panel to hold exactly this anatomy:**

- **What it does:** "Keeps the ~200 events that matter for this pull, drops the rest."
- **The subset it sees (input):** "847,000 classified events in this pull's window."
- **A sample (show real texture):** a few timestamped rows —
  `21:03:55  SPELL_DAMAGE  Thrall → boss  (schematic; real rows are denser)`
- **What it emits (output + where it goes):** "200 key events → Followership, Shape, Trends, Box score."

Every node's drill-in has this same four-part anatomy: **does · sees · sample · emits.**

---

## 5. Frames to wireframe (the actual ask)

Draw these states. Annotate generously. Low fidelity.

1. **The map at rest** — the whole pipeline for one parsed night. The spine left-to-right, the fan-out
   at the end. Each node shows its headline number (what flows through it). This is the establishing
   shot — it should make someone *want* to click.
2. **The drill-in panel (HERO FRAME)** — a node opened, holding the four-part anatomy from §4
   (does · sees · sample · emits). Resolve: does it open as a side panel, an overlay, or a split? Show
   your choice and why.
3. **A leaf/analyzer node drilled in** — e.g. **Followership → 0.73**, explicitly tied back to "these
   200 events." This is the payoff frame: raw subset → single number, made obvious.
4. **The collapse, visualized** — how do you *show* 2.14M → 200 → 1? Weighted/tapering edges? A funnel
   motif? Propose something. This is a specific viz problem; don't leave it implicit.
5. **The hand-off to Ask** — Box score → Ask. How clicking through the pipeline lands the user at
   "now ask about this night." The Explorer must return them to the product's spine, not dead-end.
6. **First-run / empty state** — before a log is parsed (or the moment one finishes). What invites the
   first click?

Optional if you have appetite:

7. **Deepest drill-down** — from a sample event in a node down to the literal raw log line it came
   from. The bottom rung of the chain; the "it really is just my log" proof.

---

## 6. Layout/interaction questions for YOU to resolve (don't over-ask — propose)

- **Orientation & the fan-out.** Left-to-right spine is the obvious default; the end fans into 4+
  analyzer nodes. How do you keep the fan from becoming clutter?
- **Drill-in placement.** Panel vs overlay vs split-view — pick one, justify it. The map should stay
  visible/contextual while a node is open if possible.
- **Flow magnitude.** Should edges encode volume (thick → thin as it funnels)? Show it.
- **The Rack as a catalog.** Analyzers are *pluggable modules.* Hint at "there could be more of these"
  (a catalog/shelf feel) without implying the user configures them in v1 (they don't — see §8).

---

## 7. The central design tension (the hard part)

This feature is, literally, an educational diagram of a data pipeline. It must **not feel like one.**
No textbook. No compiler/Airflow-DAG admin-console vibe. No "Step 1 of 6" lecture.

It should feel like a **map you want to explore** — closer to poking a machine to see what it does
than reading documentation. The understanding arrives as a *side effect of curiosity satisfied.*
Audience is a **non-technical raider** first (and the data-literate folks who'll appreciate the
rigor). If a wireframe frame reads as "instructional," redo it. This tension is the whole design
problem — solving it is the job.

---

## 8. Scope — v1 is READ + DRILL-IN, not author

- **In:** view the map, click any node, drill into real subsets/samples, follow the funnel, land at
  Ask.
- **Out (do not design):** per-node tuning or configuration, editing/reordering the pipeline, adding
  analyzers. *(That authoring surface belongs to the Tempo "lab," a different product — not here.)*
- **Later, not v1:** the beautiful look. The user's mood words are **warp-engine core**, **mind-map**,
  **flow/sequence tree**, **catalog of swappable attachments.** Treat these as aspiration: do
  function-first wireframes, but lay them out so they could be beautified into that later. Don't spend
  v1 effort on polish.

---

## 9. Success condition (how we'll judge the wireframes)

> A non-technical raider clicks through the map once and can explain, **in their own words,** how
> their raid night became the number Leopard reflected back — and asks a better question because of it.

If your wireframes make that outcome feel *inevitable*, they're right.

---

*Source of truth for the concept: `docs/pipeline-explorer.md`. Product framing:
`docs/product-vision.md`. Where it sits in the roadmap: `docs/feature-order.md`.*
