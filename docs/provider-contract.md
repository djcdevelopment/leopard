# Leopard provider contract (inference) — a hard rule

**Rule.** All inference backends are **local API providers**, configured **outside** the app. Leopard
consumes a **provider contract, not a hardware/runtime contract.**

## What a provider is
A provider is a local API endpoint speaking a known chat API:

```
{ name, baseUrl, api: "ollama" | "openai", model(s) }
```

- **Ollama is the default provider** — `http://localhost:11434`, `api: "ollama"`.
- **Intel / Vulkan / CUDA / llama.cpp-server / custom runtimes are provider *entries*** — a URL + a model
  — **never product concepts.** Leopard does not know or care what hardware or runtime sits behind a
  provider. Want the dual-B70 llama.cpp-server? Add it as a provider entry pointing at its URL. That
  backend is stood up **outside** Leopard (a Tempo / lab concern).
- Leopard **detects the default (Ollama) and enumerates its models** (`GET /api/tags`); it does **not**
  install, select, or manage runtimes.

## What this kills
The runtime-selection / startup hardware-confirm / Intel-optimization surface that crept into the Tempo
product path **does not exist in Leopard.** The product sees providers (URLs + models). The hardware story
is invisible to every surface, on purpose.

## Implication for Ask
Leopard's Ask backend is `provider.chat(messages, { stream })` against the selected provider. Default =
Ollama. Swapping providers is a config choice — invisible to Replay, Shape, Trends, Ask, Share. Specialized
runtimes (the Tempo advanced path) are just another entry in the provider list.

## How it's wired (2026-06-11)

The contract above is implemented as one provider entry in `config.json`
(`%LOCALAPPDATA%\Leopard\config.json`):

```json
{ "askProviderUrl": "http://127.0.0.1:8090", "askProviderApi": "openai" }
```

- **Null/absent = the default**: Ollama on `http://127.0.0.1:11434`, `api: "ollama"`. Existing
  installs change nothing.
- The host exposes **`/llm/*`** — a streaming reverse proxy to the configured URL. The UI's
  provider layer (`src/leopard-web/src/provider.js`) reads `askProviderApi` from `/api/config`
  and speaks the matching protocol through `/llm`: `ollama` = `/api/tags` + `/api/chat`
  (NDJSON); `openai` = `/v1/models` + `/v1/chat/completions` (SSE). The protocol parsers are
  pure and vitest-covered.
- **Dev goes through the host too** (Vite proxies `/llm` → :5280), so the configured provider
  applies identically in dev and packaged builds. The old direct `/ollama` routes remain as
  legacy aliases.
- The dual-B70 path is now a config edit: point `askProviderUrl` at the vllama facade (:8090)
  or llama-server (:8080) with `askProviderApi: "openai"`. No UI for this yet — config-file
  only, same as `liveInferenceUrl`.
- Saving a folder in Setup (`PUT /api/config` with `logDir` only) **preserves** hand-edited
  optional fields — the handler merges absent/null fields from disk (this also covers
  `liveInferenceUrl`/`liveModel`, which previously got cleared by a Setup save). To clear a
  provider entry, edit `config.json` and remove the field.
