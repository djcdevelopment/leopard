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

**The night lens — shipped 2026-06-11 evening (`58823f9`):** `BoxScore.BuildJson` emits the box
score's exact figures as structured JSON (`.night.v1.json`, `GET /api/night`) alongside the
untouched markdown blob; `NIGHT_PALETTE` + `buildNightLens` compose them property-by-property.
**Ask's default zoom is now a composer** — "Tell Leopard what matters" at the night level, with
graceful markdown fallback for pre-artifact parse vintages. The single-night story (deaths per
pull, trend, progress per boss) is the opening prompt's showcase, per the operator's framing.
**Still deferred:** the trend *lens* for Ask's trend zoom (the trend *slice* shipped in the
Explorer, `fec78da` — same data, different surface).

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

**Phase 2 — shipped (built 2026-06-11 evening, `d267c39`):** the ghosts lit up. Three new
host endpoints + parse-time caches (`/api/coverage` — per-second quality series + snaps via
a new `CoverageTimeline.ToSeconds` downsample, frames stay in-process; `/api/segments` —
movement phases; `/api/classify` — `ClassifyArtifact`, verdict-or-explicit-reason per pull,
never silence) and **five** registry flips: Coverage timeline, Formation segments, Wipe cause,
Movement meters (rides the affinity payload via a documented `meters` pseudo-api — zero new
fetches), and Shape (the existing density grid serialized as hotspot/concentration stats).
Each flip = serializer + preview + real slice options where cheap (coverage `rep`, classify
`rep`, meters `scope`/`agg`). vitest 49 → **57**, xUnit 62 → **67**; endpoints verified live;
a latent `fmt()` trailing-zero bug caught and pinned on the way.

**The over-time slices (same evening, `fec78da`):** the operator's field diagnosis after the
first live B70 investigations — "even with better data they are pretty vague" — traced to the
contract being a *snapshot*: one pull, no axis of change. Two PROGRESSION flips off existing
endpoints fixed it: **Reconciled encounter** (`progression.encounter@v1`, off `/api/night`) —
the boss-night rollup anchored on *where the selected pull sits in the sequence* — and
**Trend window** (`trend.window@v1`, off `/api/trends`) — recent-form rule rows + coherence.
**Eleven of seventeen knowledge objects live**; the remaining ghosts (moments, mechanics,
raw/replay substrates, phase-reached, reconciled pull, followership breakout) need endpoints
or design that don't exist yet. vitest → **73**.

**The provider contract, realized (`90e5bf8`):** Ask/Explorer inference is a config entry —
`askProviderUrl` + `askProviderApi` (`"ollama"` | `"openai"`) in config.json, the host's
`/llm/*` streaming proxy, dual-protocol `provider.js`. **First live B70 inference through the
product** this same evening (llama-server :8080, `tempo-b70-second`), which immediately
field-tested three fixes: detection now retries while absent (`db0ba32`), chat errors carry
the provider's message (`b0234f8`), and the B70 server needs `-Context` above its 4096
default for multi-slice contracts (operational, in the launch runbook memory).

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
