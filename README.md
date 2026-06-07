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
  compact **box score**, a **trends** artifact, a **pipeline trace**, and a **career-input**
  artifact (all keyed by file mtime).
- **Leopard** — pick a parsed night, **see the exact box score** the model will read, and **Ask**.
  The model restates those exact figures (never infers), so answers stay grounded in your data.
- **Roster** — every boss you've pulled as one **all-time career** row (attempts, kills, best %,
  direction, a progress arc), fanned in across every parsed night. Heroic/Mythic stay separate.
- **Trends** — per-boss recent-window deltas (kills / deaths / best progress / duration) plus
  per-pull coordination sparklines (followership / entropy / peak speed).
- **Pipeline** — the engine made legible: your log traced through lex → classify → segment →
  trim, with per-stage counts, a real sample at each, and the per-pull trim collapse.

All five surfaces are computed in-process via Tempo (`Tempo.Core` parser + `Tempo.Projections`)
and cached at parse time — no running engine, nothing leaves your PC.

## Layout
```
src/leopard-host/   .NET — WinForms + WebView2 + in-process Kestrel. The double-click .exe:
                    serves the UI + /api (config/logs/parse/boxscore/trends/trace/career) +
                    an /ollama proxy. References Tempo.Core (parser) + Tempo.Projections.
                    Artifact builders: BoxScore / TrendsArtifact / PipelineTrace / CareerRoster.
src/leopard-host.Tests/  xUnit — CareerRoster aggregation + PipelineTrace conservation/parity,
                    against Tempo's committed combat-log fixtures.
src/leopard-web/    UI — Vite + React. The five tabs above; built to a static bundle for ship.
tools/              make-boxscore.mjs — standalone box-score generator (the host does this in C#).
dependencies/       tempo-engine/seam.md — how Leopard reaches the Tempo engine.
docs/               product-vision.md, feature-order.md, career-roster.md, pipeline-explorer.md,
                    provider-contract.md, local-inference-runbook.md.
```

## Dev
```powershell
# .NET host (serves UI + API on http://localhost:5280, opens the desktop window)
dotnet run --project src/leopard-host

# headless (serve the API only — no window; for cache backfills, scripting, smoke checks)
dotnet run --project src/leopard-host -- --headless

# tests (CareerRoster aggregation + PipelineTrace conservation/parity)
dotnet test src/leopard-host.Tests

# UI hot-reload (optional; proxies /api + /ollama to the host) — http://localhost:5273
cd src/leopard-web; npm install; npm run dev

# build the shipped UI bundle into the host (vite outputs straight to wwwroot):
cd src/leopard-web; npm run build      # -> ../leopard-host/wwwroot
dotnet build src/leopard-host          # refresh the static-assets manifest for the new asset hashes
```

**Requires:** .NET 9 SDK · Node 20+ · local Ollama with a model pulled
(`ollama pull qwen2.5:14b-instruct`) · the Tempo repo at `../../World of Warcraft/tempo`.
