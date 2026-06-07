# Leopard — feature order

The build sequence, ordered by the **mission** (demystify + empower) — not by what's
technically nearest. Each surface is a progressively richer reflection of one reality; we
build the ones that make data *less scary* first.

## Validated in the field (2026-06-02 demo)
Shown to guild members in **finance, accounting, data science, and engineering.** They
recognized **Shapes** and **Trends** on sight — *"oh, clever"* — because those surfaces speak
the language of people who already think in data. The thesis (*make data not scary;
demystify + empower*) landed with exactly the audience that would know a real one from a toy.
That demo reset the order below.

## Order

### Shipped — Leopard Zero (the spine)
Local Ask grounded in a pre-computed box score, on your own machine. The .NET `.exe`:
**Setup** (folder picker + log grid + PARSE) and **Leopard** (pick a parsed night → see the
exact evidence → Ask). Inference = Ollama. Committed (`dee5f19`).

### Shipped — reflection surfaces (built 2026-06-05, branch `feat/trends-pipeline-explorer`)
Both proved a reusable seam: compute an artifact at parse time (in-process via
`Tempo.Projections`), cache it next to the box score, serve it to a thin React tab —
**zero Tempo engine changes.** Verified live against real logs (the data path); a visual
pass is still pending.
1. **Trends** — per-encounter rule-row windows (kills / avg deaths / best progress / pull
   duration, each with a better/worse/flat delta) + per-pull coherence sparklines
   (followership / entropy / peak speed), via `TrendsProjection`. The recent window is now
   **user-selectable — 4 / 6 / 8 / 10 pulls** (default 6 = Tempo parity), precomputed per size
   in `TrendsArtifact` at parse time (no recompute on toggle). The full arc still lives in the
   Roster, below.
2. **Pipeline Explorer (read-only v1)** — the engine made legible: the real stages
   (lex → classify → segment → trim) with per-stage counts, a real sample at each, and the
   per-pull trim collapse (e.g. 86,426 → 1,876 events), fanning out to projections. The
   `does · sees · sample · emits` drill-in, on the user's own data. `PipelineTrace` re-walks
   stages 1–3 (the `PretrimCounts` path) for the pre-trim substrate `Parse` discards.
   *Authoring is still ahead* — v1 is legibility, the on-ramp (see `pipeline-explorer.md`).

### Next — the Roster (multi-run / multi-boss), see `career-roster.md`
The career view: every boss this tier as a row — **all-time** kills / best % / attempts /
direction — with `Heroic` and `Mythic` as **separate careers** (Tempo's `careerId` splits them).
A **fan-in across runs**: the cross-night math already exists (`CareerProjection` merges a boss
across sessions, re-numbers 1..N); the only new pieces are a tiny per-night career-input
artifact + a `/api/career` aggregator. The next worked example of the teach-to-author chain.

### Later
- **Shape** — the long-exposure fight signature. *Perspective.* The engine ships a seven-view
  `ShapeProjection` suite; **v1 = the density heatmap (where the raid stood, per pull) + the
  kill-vs-wipe contrast (`wkdelta`)**, with affinity/groups/score/meter deferred and `trend`
  excluded (Trends owns time-series). Same parse-time-artifact seam as Trends. Design brief (UX
  hand-off, grounded in the real DTOs): `pipeline-explorer-design-brief.md`'s sibling,
  `shape-design-brief.md`.
- The **authoring** surface — making pipeline nodes / projections swappable (the Rack), the
  destination the read-only Explorer is the on-ramp to.

### Parked
- **Replay (player display).** The leader/guild lens — already built several times in React,
  and not the empowerment core. Thaw when the guild-leader perspective is the focus.
- **Share**, Seismograph, deep Guild Ops.

## The throughline
Replay = evidence · Shape = perspective · Trends = context · Ask = exploration ·
**Pipeline Explorer = how it all connects.** Each drills down to the layer beneath; together
they teach the chain by *delight*, never by lecture.
