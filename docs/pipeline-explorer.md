# Pipeline Explorer — concept (the flagship empowerment surface)

**Status:** Concept, locked 2026-06-02. The deepest expression of Leopard's mission.

## What it is
The **engine made visible** — a clickable dataflow map of how your raw combat log becomes the
numbers and pictures Leopard shows. It is the product's chain — `movement → events → logs →
data structures → projections → insight` — turned into something you can *explore*.

It's also Tempo's founding intent realized for users: **make the classical methods touchable
for non-mathematicians.** Not a lecture — a tool you poke that shows *your own data* flowing
through real transforms.

## Why (the demo told us)
Data-literate people recognized Shapes/Trends instantly because they could *see the move.* The
Pipeline Explorer makes that move legible to **anyone**: it shows, concretely, how a multi-GB
log collapses into a ~200-event window and then into a single coordination number. Demystify +
empower, made structural.

## Grounded in the real engine (not a cartoon)
Nodes map to transforms that already exist:

```
Raw log → Lex → Classify → Segment (pulls) → Trim (~200 key events)
                                                  ├─ Followership / Percolation / Flex   ← the Rack
                                                  ├─ Shape      (long-exposure signature)
                                                  ├─ Trends     (signals over time)
                                                  └─ Box score  → Ask
```
- The "first filter that keeps the events you want" = **Lex / Classify / Trim**.
- The "projection layer — delta over a window" = the **projections + Rack analyzers**.
- The "catalog of attachments" = **Tempo.Rack** — pluggable analyzer modules — as clickable nodes.

## The load-bearing magic: drill-in to real data
Clicking a node is the whole point. It shows the funnel **with the user's actual numbers and a
sample**:

> 2,140,000 lines → 847,000 classified events → **200 kept** for this pull → **followership 0.73**,
> computed from *these 200 events.*

The *"oh — that's how my log becomes that number"* moment is the empowerment. The graph topology
is only the frame; the drill-in to real subsets is the product.

## Buildable shape (function-first)
- **Engine** emits a *pipeline trace* for a parsed log: per-stage counts + a small data sample at
  each node. The pieces exist — the parser stages, the `--pretrim-counts` instrument, the
  projection outputs.
- **UI** renders a plain logical dataflow graph (boxes + arrows) with **click → drill-in panel**
  (what this node does · the subset it sees · a sample · what it emits).
- Logical/functional first. The mind-map look, the animation, the "warp-engine" polish — later.

## Success condition
A non-technical raider clicks through the map once and can explain, in their own words, how their
raid night became the number Leopard reflected back — and asks a better question because of it.

## Not now
Pretty visuals; per-node tuning/configuration (that's the Tempo lab surface); editing the
pipeline. v1 is **read + drill-in to understand**, not author.
