# Leopard 🐆

A **reflection engine** for your World of Warcraft raid data: ask questions of your own
combat logs, on your own machine, with a local model. No cloud — nothing leaves your PC.

Leopard is the **product**. It consumes the **Tempo** engine (the combat-log parser) in-process
and a local **Ollama** provider for inference. Specialized inference runtimes
(Intel/Vulkan/custom) are Tempo/lab concerns, not product concepts — see
[`docs/provider-contract.md`](docs/provider-contract.md).

## What it does
- **Setup / Configuration** — point at your WoW Logs folder, see your logs in a grid, select
  one or many, **PARSE**. Parsing runs Tempo's parser in-process and computes a compact,
  pre-computed **box score** per night.
- **Leopard** — pick a parsed night, **see the exact box score** the model will read, and **Ask**.
  The model restates those exact figures (never infers), so answers stay grounded in your data.

## Layout
```
src/leopard-host/   .NET — WinForms + WebView2 + in-process Kestrel. The double-click .exe:
                    serves the UI + /api (config/logs/parse/boxscore) + an /ollama proxy,
                    and references Tempo.Core for parsing + BoxScore.cs for the box score.
src/leopard-web/    UI — Vite + React. The two tabs above; built to a static bundle for ship.
tools/              make-boxscore.mjs — standalone box-score generator (the host does this in C#).
dependencies/       tempo-engine/seam.md — how Leopard reaches the Tempo engine.
docs/               product-vision.md, provider-contract.md.
```

## Dev
```powershell
# .NET host (serves UI + API on http://localhost:5280)
dotnet run --project src/leopard-host

# UI hot-reload (optional; proxies /api + /ollama to the host) — http://localhost:5273
cd src/leopard-web; npm install; npm run dev

# build the shipped UI bundle into the host:
cd src/leopard-web; npm run build      # then copy dist/* -> ../leopard-host/wwwroot/
```

**Requires:** .NET 9 SDK · Node 20+ · local Ollama with a model pulled
(`ollama pull qwen2.5:14b-instruct`) · the Tempo repo at `../../World of Warcraft/tempo`.
