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

### Shipped — the Roster (multi-run / multi-boss), see `career-roster.md`
The career view: every boss this tier as a row — **all-time** kills / best % / attempts /
direction — with `Heroic` and `Mythic` as **separate careers** (Tempo's `careerId` splits them).
A **fan-in across runs**: `CareerProjection` merges a boss across sessions (re-numbers 1..N); the
new pieces were a tiny per-night career-input artifact + the `/api/career` aggregator.

### Shipped — Shape (built 2026-06-07)
The sixth surface. **v1 = density heatmap (where the raid stood, per pull) + a CAREER-scoped
kill-vs-wipe contrast (`wkdelta`, fanned across nights)**; affinity/groups/score/meter deferred,
`trend` excluded (Trends owns time-series). Density caches per night (`.shape.v1.json`); wkdelta
computes live at `/api/shape/wkdelta` from the fanned career inputs. The data check that set this
scope: per-night a boss is all-kills or all-wipes, but 48/119 careers have both. **v2a shipped**:
a within-night "all N attempts" overlay (client-side sum of the per-pull grids). Brief:
`shape-design-brief.md`. *Parked:* the all-time career overlay — normalized-space aggregation
washes out across nights/rooms; a true career signature needs Tempo raw pre-normalization
positions (a separate effort), not a quick fan-in.

### Shipped — Query-builder ensure + compose rungs (built 2026-06-07 / 2026-06-09, branch `inventory-canonical-context`)
The **ensure rung** locked the `display==send` invariant structurally: `CanonicalContext` is a
frozen value object where `render()` is **defined in terms of** `serialize()` — the bytes cannot
drift. Carries `schemaVersion`, `scopeLabel`, `digest{sha256, propertyCount}` from day one.
18 vitest tests guard it (byte-exact on all 3 zooms, determinism, frozen-value mutation rejection,
SHA-256 vs Node `crypto`). Alongside: `docs/property-inventory.md` (all 7 artifacts, stable
versioned IDs `raid.pull.bossEndPctHp@v1` …, every field traced to its C# source line) and
`docs/query-builder-design-prompt.md` (locked constraints: versioned envelope, canonical
serialization, graph-first storage, lens framing, provenance, confidence propagation, UserIntent).

The **compose rung v1** (career lens): `lens.js` — a pure function `buildCareerLens(boss, selectedIds)
→ {xml, displayItems, propertyCount}` over the `CAREER_PALETTE` (9 properties with stable IDs,
provenance `exact|derived`, and computable confidence). AskPanel now shows a **"Tell Leopard what
matters" checkbox palette** in career zoom; the evidence panel becomes **"What Leopard knows"** — an
assembled list the user watches build before asking. The model receives the versioned XML; the user
sees the same set of properties in human-readable form. Demystification moment: the grounding chain
is visible and composable, not hidden behind a text blob.

**Deferred to a future session:** night and trend lenses (require structured C# emit alongside the
current markdown blobs; documented in `docs/property-inventory.md`).

### Later
- The **authoring** surface — making pipeline nodes / projections swappable (the Rack), the
  destination the read-only Explorer is the on-ramp to. Design prompt: `docs/rack-design-prompt.md`.

### Parked
- **Replay (player display).** The leader/guild lens — already built several times in React,
  and not the empowerment core. Thaw when the guild-leader perspective is the focus.
- **Share**, Seismograph, deep Guild Ops.

## The throughline
Replay = evidence · Shape = perspective · Trends = context · Ask = exploration ·
**Pipeline Explorer = how it all connects.** Each drills down to the layer beneath; together
they teach the chain by *delight*, never by lecture.
