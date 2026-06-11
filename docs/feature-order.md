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

### Shipped — Query-builder ensure + compose rungs (built 2026-06-07 / 2026-06-09, merged to master 2026-06-10 via PR #1)
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

### Shipped — Live + the RaidUI math port (built 2026-06-09 evening, merged 2026-06-10)
The seventh surface, in two intertwined strands (13 commits, `0bea285..332843d`):

**Live** — between-pull insight on Tempo's live ingest front. `LiveSession.cs` consumes
`Tempo.Core.Ingest.FileSystemLogMonitor` in-process (zero Tempo changes): a pull ends →
three-layer evidence (the pull + tonight's trajectory + the cached all-time career) → pre-generated
insight on the 2nd B70 (OpenAI-style endpoint, config `liveInferenceUrl`) → **LiveTab**, the expert
review desk (card + evidence disclosure + two named feedback axes: *useful* / *grounded* + comment).
Every lifecycle event appends to `live-insight.jsonl` — the replayable eval corpus for the
critic-loop work, and the file bridge discoverlay tails for over-the-game delivery.
Brief: `live-insight-design-brief.md`.

**The math port** — the entire RaidUI JS analysis corpus, ported to C# artifact modules in
leopard-host (ShapeArtifact pattern, RaidUI `__tests__` as parity oracle): `SignalsArtifact`
(six-signal pack — coverage / spacing / hp-variance / deaths-per-sec / followership / entropy),
`PullDiff` (this-pull-vs-best diff), `WipeClassifier` (called-wipe gate, fatality tiers, consensus
inflection — feeds a "called wipe ⇒ don't coach" prompt gate), `CoverageTimeline` (per-healer
quality + snaps + named-offender attribution), `MovementAffinity` + `CoverageGaps` (group structure),
`FormationSegments` (movement phases), `PlayerScores` (role-weighted profiles + archetypes),
`ParticipantMeters`, `PullDivergence`. All feed Live's evidence block and per-night cached
artifacts (`.signals.v1.json`, `.players.v1.json`, `.affinity.v1.json`). xUnit suite 9 → **62**.
The RaidUI port audit is **closed** — remaining JS deliberately not ported, with reasons.

### Shipped — Explorer (knowledge-library IDE, phase 1) — built 2026-06-11 (`0ecba13`)
The eighth surface, and the **compose rung generalized**: where the career lens composed nine
roster properties, the Explorer composes *knowledge objects* — the ported RaidUI math as
selectable, shapeable context slices. Design source: Derek's mockup (HomeSite-2.5a-1997 IDE
density in Leopard's palette). Three panes: a **knowledge tree** (`knowledge.js` registry —
Pulse/six signals, Cohesion graph, Reaction spread/player scores, Pull diff **live**; the full
taxonomy ghosted with `status:'ghost'` so phase 2 only flips entries), a **compiled.context
editor** (`contract.js` — slice XML `<context><pull><slice object scope rep agg time tok/>`
with lens.js canonicalization + digest-over-inner rules, display-only payload folding) with
**Run Investigation** through the same `buildMessages`/`chatStream` path AskPanel uses
(display==send structural via `ContextBuilder`), and a **Properties inspector** (provenance
metadata from the registry; slice dropdowns recompile digest/tok live — real payload shaping
where cheap, honest annotations otherwise; RaidUI-style previews). vitest 18 → **37**.
ADR-0007 (client-side slice compiler), ADR-0008 (exe-relative wwwroot).

**Phase 2 (planned):** `/api/coverage` / `/api/segments` / `/api/classify` endpoints + caches
(the C# modules exist and are tested — plumbing only), then ghost→live flips; Movement meters
and Shape are cheap flips (data already served). Plan:
`C:\Users\derek\.claude\plans\jaunty-coalescing-pine.md`.

### Later
- The **authoring** surface — making pipeline nodes / projections swappable (the Rack), the
  destination the read-only Explorer is the on-ramp to. Design prompt: `docs/rack-design-prompt.md`.

### Parked
- **Replay (player display).** The leader/guild lens — already built several times in React,
  and not the empowerment core. Thaw when the guild-leader perspective is the focus.
- **Share**, Seismograph, deep Guild Ops.

## The throughline
Replay = evidence · Shape = perspective · Trends = context · Ask = exploration ·
**Pipeline Explorer = how it all connects** · **Live = the loop closing in real time** ·
**Explorer = the question, composed** (the query-builder north star made visible).
Each drills down to the layer beneath; together they teach the chain by *delight*, never
by lecture.
