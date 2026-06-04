# Tempo engine seam — how Leopard reaches the engine

**Leopard = product. Tempo = engine.** Leopard reaches Tempo two ways; Leopard Zero uses #1 only.

## 1. One-shot CLI — the DEFAULT (no running engine)
`Tempo.Diagnostics` parses a log in-process via `ParserPipeline.Parse(logPath)`. No ViewerApi, no WinUI,
no Kestrel. Just `dotnet run`.

- **Summary table (stdout):**
  `dotnet run --project src/Tempo.Diagnostics -- --log <path> --summarize`
  → session / encounter / pull table incl. M+ (`pull# · outcome · mm:ss · deaths · boss%`).
- **LLM evidence prefix (Markdown):**
  `dotnet run --project src/Tempo.Diagnostics -- --log <path> --export-evidence <out.md>`
  → session-level roster + encounters + pulls (outcome / duration / deaths / BossEndPctHp / participants),
  explicitly built "for explorer-lens prefix-cache workflows against a local LLM." **Coarse** (session-level,
  no per-event citations) — but it is *real* grounding (the model sees the actual roster, pulls, outcomes).

## 2. Running ViewerApi — ADVANCED only (not needed for Leopard Zero)
Tempo.Host serves Kestrel on `127.0.0.1`: `/api/encounters`, `/api/pulls/{id}/replay`, `/api/trends/*`,
`/api/shape/*`, `/api/lantern/ask`, `/api/live` (SSE). For live + the richer surfaces later.

## The grounding path (the bar)
The proven, calibrated grounding logic is **`EvidenceRetriever.Retrieve(question, cohort, ParseResult,
momentsByPullId) → EvidenceBundle`** (`Tempo.Agents.Lantern`) — per-pull, citation-bearing, with hard-won
calibration baked in (the 50→200 `MaxItems` citation-starvation finding; the heal-event fix; friendly-name
+ GUID dual surfacing). It powers Lantern's Tier-4 citation RAG.

It is a **pure function over a `ParseResult` + a moments map** — both produced one-shot by
`ParserPipeline.Parse` + `MomentsAnalyzer`. So citation-grade grounded evidence can be produced **with no
running engine.**

## Two-tier grounding → an even smaller first step
- **First light (ZERO Tempo changes):** `--export-evidence` session prefix + local Ollama → an answer
  grounded in your real session. Proves the thesis (*my data, my machine, a true answer*) immediately, with
  nothing new built engine-side.
- **The bar (one small Tempo addition):** add a one-shot mode
  `Tempo.Diagnostics --ask-evidence "<question>" [--pull <id>] [--out bundle.json]` that runs
  `Parse → moments → EvidenceRetriever.Retrieve` and emits the `EvidenceBundle` JSON. A thin wrapper over
  proven code; gives per-pull, citation-grade grounding without a running engine.

## Ollama (the local model)
- Detect + enumerate installed models: `GET http://localhost:11434/api/tags`.
- Chat (stream): `POST /api/chat` (messages) with `"stream": true`.
- If absent: the warm onboarding flow — install Ollama → `ollama pull <model>` → confirm.

## Bottom line
The seam is **largely already built.** One-shot parse + evidence export exist today; the grounded retriever
is proven and runs one-shot from a `ParseResult`. The *only* engine-side addition Leopard Zero's "bar"
version needs is the thin `--ask-evidence` CLI wrapper over `EvidenceRetriever`. Everything else is
`leopard-web` + the local Ollama loop. We can even prove first light with **zero** Tempo changes.
