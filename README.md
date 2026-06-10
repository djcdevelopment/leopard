# Leopard 🐆

A **reflection engine** for your World of Warcraft raid data: ask questions of your own
combat logs, on your own machine, with a local model. No cloud — nothing leaves your PC.

Leopard is the **product**. It consumes the **Tempo** engine (the combat-log parser) in-process
and a local **Ollama** provider for inference. Specialized inference runtimes
(Intel/Vulkan/custom) are Tempo/lab concerns, not product concepts — see
[`docs/provider-contract.md`](docs/provider-contract.md).

## What it does
- **Setup / Configuration** — point at your WoW Logs folder, see your logs in a grid, select
  one or many, **PARSE**. Parsing runs Tempo's parser in-process and, per night, caches a
  compact **box score**, a **trends** artifact, a **pipeline trace**, a **career-input** artifact,
  and a **shape** artifact (per-pull heatmaps; all keyed by file mtime).
- **Leopard** — pick a parsed night and zoom (night box score / boss career arc / recent form), then
  **Ask**. Career zoom shows **"Tell Leopard what matters"** — a property palette that assembles the
  XML context the model reads, visible as a "What Leopard knows" list. `render()===serialize()`:
  the displayed evidence and the model payload are the same bytes (`CanonicalContext`).
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

All seven surfaces are computed in-process via Tempo (`Tempo.Core` parser + `Tempo.Projections`)
and cached at parse time — no running engine, nothing leaves your PC.

## Layout
```
src/leopard-host/   .NET — WinForms + WebView2 + in-process Kestrel. The double-click .exe:
                    serves the UI + /api (config/logs/parse/boxscore/trends/trace/career/shape/
                    signals/players/diff + live/status·insight·feedback) + an /ollama proxy.
                    References Tempo.Core (parser+ingest) + Tempo.Projections.
                    Artifact builders: BoxScore / TrendsArtifact / PipelineTrace / CareerRoster /
                    ShapeArtifact / SignalsArtifact / PullDiff / WipeClassifier / CoverageTimeline /
                    MovementAffinity / FormationSegments / PlayerScores / ParticipantMeters /
                    PullDivergence — the full RaidUI math port (ADR-0005).
                    LiveSession.cs — the between-pull insight brain on Tempo's live ingest (ADR-0006).
src/leopard-host.Tests/  xUnit (62 tests) — CareerRoster aggregation, PipelineTrace conservation/
                    parity, and per-module invariant suites for every ported analysis module,
                    against Tempo's committed combat-log fixtures + RaidUI-derived oracles.
src/leopard-web/    UI — Vite + React. The seven tabs above; built to a static bundle for ship.
                    context.test.js — vitest (18 tests): render===serialize byte-exact on all 3
                    zoom shapes; determinism; frozen-value mutation rejection; SHA-256 validation.
                    lens.js — the career lens composer (property palette → versioned XML).
tools/              make-boxscore.mjs — standalone box-score generator (the host does this in C#).
dependencies/       tempo-engine/seam.md — how Leopard reaches the Tempo engine.
docs/               product-vision.md, feature-order.md, career-roster.md, pipeline-explorer.md,
                    provider-contract.md, local-inference-runbook.md, property-inventory.md
                    (versioned property palette for the query builder — disk-confirmed),
                    query-builder-design-prompt.md, rack-design-prompt.md (authoring surface),
                    live-insight-design-brief.md, signals-artifact-port-brief.md,
                    adr/ (ADR-0001..0006).
```

## Dev
```powershell
# .NET host (serves UI + API on http://localhost:5280, opens the desktop window)
dotnet run --project src/leopard-host

# headless (serve the API only — no window; for cache backfills, scripting, smoke checks)
dotnet run --project src/leopard-host -- --headless

# tests (62 — roster/trace + invariant suites for all ported analysis modules)
dotnet test src/leopard-host.Tests

# UI hot-reload (optional; proxies /api + /ollama to the host) — http://localhost:5273
cd src/leopard-web; npm install; npm run dev

# web unit tests (vitest; 18 tests — CanonicalContext display==send invariant)
cd src/leopard-web; npm test

# build the shipped UI bundle into the host (vite outputs straight to wwwroot):
cd src/leopard-web; npm run build      # -> ../leopard-host/wwwroot
dotnet build src/leopard-host          # refresh the static-assets manifest for the new asset hashes
```

**Requires:** .NET 9 SDK · Node 20+ · local Ollama with a model pulled
(`ollama pull qwen2.5:14b-instruct`) · the Tempo repo at `../../World of Warcraft/tempo`.
