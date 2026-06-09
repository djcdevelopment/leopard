# The Rack — design prompt (for iteration)

**This is a starting prompt, not a finished brief.** It frames the problem and surfaces the
load-bearing questions so you (Derek) can iterate before it becomes a brief like
`shape-design-brief.md`. The open questions in section 5 are the point.

---

## 1. What the Rack is

The **authoring surface**: where a user makes the pipeline's nodes and projections **swappable** —
the destination the read-only **Pipeline Explorer** was deliberately built as the on-ramp to. It is
the payoff of the foundational goal (memory `foundational-goal-teach-to-author`): *the pre-built
surfaces (Trends / Pipeline / Roster / Shape) are **exhibits**; the destination is users building
their **own** parser steps / projections / substrates.* The Rack is where Leopard stops being a
thing you look at and becomes a thing you author with.

The chain the whole product teaches:

> movement -> events -> logs -> data structures -> **projections** -> visualizations -> insight

Trends/Shape/Roster each *own one projection* and show it. The Rack hands the **projection step
itself** to the user.

## 2. Why it's the destination, not another exhibit

Every surface so far consumes Tempo as a fixed library (zero engine changes) and renders a
precomputed artifact. That's an exhibit: the move is made *for* the user. The Rack inverts it — the
user makes the move. That is the difference between "here's followership" and "here's how
followership is computed, now change it and watch the number move / write your own." If we only ever
ship exhibits, we've built a nice dashboard, not the teach-to-author tool.

## 3. The central design tension (the hard part — name it before drawing)

Authoring is inherently closer to an IDE/console than to the delight-first exhibits, but the tone
rule still holds: **the instant it feels like teaching (or like a config screen), it dies.** The
whole design problem is: *how do you let someone author a projection without it becoming a
spreadsheet of knobs or a coding tutorial?* The Pipeline Explorer's "poke a machine, understanding
arrives as a side effect" is the bar — the Rack has to keep that feel while doing something far more
powerful.

## 4. The architecture question that gates everything

"Swappable" has a wide range, and where we land changes the whole build:

- **(a) Tune** — adjust a projection's parameters (e.g. Trends window already does 4/6/8/10; the
  density grid is 32x16; affinity threshold is yards) and watch the artifact recompute. *Smallest;
  no engine extensibility needed — these params already exist in `ShapeProjection`/`TrendsProjection`.*
- **(b) Toggle / reorder** — turn projections (the Rack analyzers: Followership / Percolation /
  Flex) on/off, choose which run, reorder where they sit. *Medium; needs the host to run projections
  selectively, still no user code.*
- **(c) Fork / author** — let the user write or edit a projection (a formula, a small script) over
  the trimmed event stream and see it run on their data. *Largest; needs a real extensibility layer
  — a scripting/plugin seam over Tempo's contracts. This is the true "author your own."*

Leopard today has **no user-extensibility seam** — it links Tempo at compile time. (c) is the
foundational goal but is a genuine architectural shift (sandboxed scripting? a projection DSL?
exposing `Tempo.Projections` contracts to user code?). (a)/(b) are reachable on the current seam and
could be the on-ramp *within* the on-ramp.

## 5. Open questions for you to iterate

1. **Where on the (a)->(b)->(c) ladder is "the Rack v1"?** Tune-only is shippable soon and still
   teaches (change the input, watch the output move). Author-your-own is the real destination but is
   a big architectural bet. Is v1 the tuning rung, with authoring as the named north of it?
2. **What's the unit the user manipulates?** A projection (Followership)? A pipeline node (Trim)? A
   parameter? The "Rack" metaphor implies *swappable attachments* — what's an attachment, concretely?
3. **What does "see it run on my data" look like** without becoming a results table — does a tuned
   projection re-render the *existing* surface (Trends/Shape) live, or get its own canvas?
4. **For (c): what's the authoring medium** that a non-programmer raider could use — a formula bar?
   a visual node graph? a constrained DSL? Or is the Rack explicitly for the data-literate tier
   (the finance/DS/eng guildies who recognized Shapes on sight), accepting it's not for everyone?
5. **How far can we go with zero Tempo changes**, and where does the engine have to grow an
   extensibility contract? (This likely decides whether the Rack is a near-term surface or a
   next-quarter architecture project.)
6. **What's the smallest thing that delivers the "I authored this" moment** — the Rack's equivalent
   of Shape's "oh, that's where we stood"? That moment is the design target; everything else serves it.

## 6. Grounding (real, not a cartoon — same standard as the other briefs)

The pipeline is real and already legible in the Pipeline Explorer: lex -> classify -> segment ->
trim -> projections. Real projections exist (`ShapeProjection`, `TrendsProjection`,
`CareerProjection`) with real, already-exposed parameters (window sizes, grid dims, thresholds). Any
Rack v1 on rung (a) can be built on these today. Don't invent a node or a knob that isn't real.

---

*North star: `foundational-goal-teach-to-author`. On-ramp it completes: `pipeline-explorer.md`.
Sibling briefs for structure/voice once this graduates from prompt to brief:
`pipeline-explorer-design-brief.md`, `shape-design-brief.md`.*
