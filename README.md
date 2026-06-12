# Leopard 🐆

A **reflection engine** for your World of Warcraft raid data: ask questions of your own
combat logs, on your own machine, with a local model. No cloud — nothing leaves your PC.

Leopard is the **product**. It consumes the **Tempo** engine (the combat-log parser) in-process
and a local inference **provider** — Ollama by default, or any OpenAI-shaped endpoint
(llama-server, vllama) via a config entry (`askProviderUrl`/`askProviderApi`, ADR-0010).
Specialized inference runtimes (Intel/Vulkan/custom) are Tempo/lab concerns, not product
concepts — see [`docs/provider-contract.md`](docs/provider-contract.md).

## What it does
- **Setup / Configuration** — point at your WoW Logs folder, see your logs in a grid, select
  one or many, **PARSE**. Parsing runs Tempo's parser in-process and, per night, caches eleven
  artifacts keyed by file mtime: a compact **box score**, **trends**, a **pipeline trace**,
  **career-input**, **shape** (per-pull heatmaps), **signals** (the six-signal pack),
  **affinity** (movement groups + meters), **players** (scores + archetypes), **coverage**
  (healing-coverage quality), **segments** (formation phases), and **classify** (wipe verdicts).
- **Leopard** — pick a parsed night and zoom (night / boss career arc / recent form), then
  **Ask**. Night AND career zooms show **"Tell Leopard what matters"** — a property palette that
  assembles the XML context the model reads, visible as a "What Leopard knows" list (the night
  lens composes the structured box score: deaths per pull, trends, progress per boss).
  `render()===serialize()`: the displayed evidence and the model payload are the same bytes
  (`CanonicalContext`).
- **Roster** — every boss you've pulled as one **all-time career** row (attempts, kills, best %,
  direction, a progress arc), fanned in across every parsed night. Heroic/Mythic stay separate.
- **Trends** — per-boss recent-window deltas (kills / deaths / best progress / duration) plus
  per-pull coordination sparklines (followership / entropy / peak speed).
- **Shape** — the long-exposure heatmap of where the raid stood on a pull, plus a career-scoped
  kill-vs-wipe contrast (what your kills had that your wipes didn't), all-time across every night.
- **Pipeline** — the engine made legible: your log traced through lex → classify → segment →
  trim, with per-stage counts, a real sample at each, and the per-pull trim collapse.
- **Live** — while you raid (`/combatlog` on), Leopard detects each pull ending and has a
  grounded insight ready before you alt-tab: three-layer evidence (this pull + tonight's
  trajectory + the all-time career) sent to a local OpenAI-style endpoint, reviewed on a card
  with two named feedback taps (*useful* / *grounded*). Every event lands in
  `live-insight.jsonl` — replayable, and the file bridge for over-the-game overlay delivery.
- **Explorer** — the knowledge-library IDE (the query builder made visible): a tree of
  knowledge objects (**eleven live**: six-signal Pulse, Cohesion graph, player-score Reaction
  spread, Pull diff, Coverage timeline, Formation segments, Wipe cause, Movement meters,
  Shape, and the over-time pair — Reconciled encounter (the boss-night arc, anchored on where
  the selected pull sits) + Trend window (recent-form deltas); the remaining ghosts await
  endpoints that don't exist yet), composed into a **compiled.context** slice-XML contract you
  watch assemble (digest + per-slice token weights), shaped per-slice in a Properties
  inspector, and run as an **Investigation** against the local model. Same display==send
  guarantee as Ask — the editor shows the exact bytes sent.

All eight surfaces are computed in-process via Tempo (`Tempo.Core` parser + `Tempo.Projections`)
and cached at parse time — no running engine, nothing leaves your PC.

## Layout
```
src/leopard-host/   .NET — WinForms + WebView2 + in-process Kestrel. The double-click .exe:
                    serves the UI + /api (config/logs/parse/boxscore/night/trends/trace/career/
                    shape/signals/players/diff/coverage/segments/classify + live/status·insight·
                    feedback) + /llm (streaming proxy to the CONFIGURED provider, ADR-0010;
                    /ollama kept as the legacy fixed-target alias).
                    References Tempo.Core (parser+ingest) + Tempo.Projections.
                    Artifact builders: BoxScore / TrendsArtifact / PipelineTrace / CareerRoster /
                    ShapeArtifact / SignalsArtifact / PullDiff / WipeClassifier / ClassifyArtifact /
                    CoverageTimeline / MovementAffinity / FormationSegments / PlayerScores /
                    ParticipantMeters / PullDivergence — the full RaidUI math port (ADR-0005),
                    cached per night at parse time (chart-resolution series, ADR-0009).
                    LiveSession.cs — the between-pull insight brain on Tempo's live ingest (ADR-0006).
                    Serves the UI from an exe-relative wwwroot, copied to bin on build (ADR-0008).
src/leopard-host.Tests/  xUnit (68 tests) — CareerRoster aggregation, PipelineTrace conservation/
                    parity, per-module invariant suites for every ported analysis module, and
                    per-night artifact shape tests (NightArtifactTests, incl. .night.v1),
                    against Tempo's committed combat-log fixtures + RaidUI-derived oracles.
src/leopard-web/    UI — Vite + React. The eight tabs above; built to a static bundle for ship.
                    vitest (73 tests): context.test.js (render===serialize byte-exact),
                    lens.test.js (career + night lenses: canonical order, digest, provenance,
                    confidence, absence rules), knowledge.test.js + contract.test.js (Explorer
                    registry + slice compiler: golden XML for all eleven live objects,
                    digest stability, explicit-absence lines), provider.test.js (the
                    dual-protocol parsers — Ollama NDJSON / OpenAI SSE).
                    lens.js — the career lens composer (property palette → versioned XML).
                    knowledge.js / contract.js — the Explorer's object registry + slice
                    compiler (ADR-0007); Explorer*.jsx — the knowledge-IDE surface.
tools/              make-boxscore.mjs — standalone box-score generator (the host does this in C#).
dependencies/       tempo-engine/seam.md — how Leopard reaches the Tempo engine.
docs/               product-vision.md, feature-order.md, career-roster.md, pipeline-explorer.md,
                    provider-contract.md, local-inference-runbook.md, property-inventory.md
                    (versioned property palette for the query builder — disk-confirmed),
                    query-builder-design-prompt.md, rack-design-prompt.md (authoring surface),
                    live-insight-design-brief.md, signals-artifact-port-brief.md,
                    adr/ (ADR-0001..0010).
```

## Dev
```powershell
# .NET host (serves UI + API on http://localhost:5280, opens the desktop window)
dotnet run --project src/leopard-host

# headless (serve the API only — no window; for cache backfills, scripting, smoke checks)
dotnet run --project src/leopard-host -- --headless

# tests (68 — roster/trace + invariant suites + per-night artifact shapes)
dotnet test src/leopard-host.Tests

# UI hot-reload (optional; proxies /api + /ollama to the host) — http://localhost:5273
cd src/leopard-web; npm install; npm run dev

# web unit tests (vitest; 73 tests — contexts, both lenses, Explorer compiler, provider protocols)
cd src/leopard-web; npm test

# build the shipped UI bundle into the host (vite outputs straight to wwwroot):
cd src/leopard-web; npm run build      # -> ../leopard-host/wwwroot
dotnet build src/leopard-host          # REQUIRED: copies wwwroot into bin (the exe serves the
                                       # bin copy — skipping this serves stale assets; ADR-0008)
```

**Requires:** .NET 9 SDK · Node 20+ · a local model provider — Ollama with a model pulled
(`ollama pull qwen2.5:14b-instruct`), or any OpenAI-shaped server via config
(`askProviderUrl`/`askProviderApi` in `%LOCALAPPDATA%\Leopard\config.json`) ·
the Tempo repo at `../../World of Warcraft/tempo`.
