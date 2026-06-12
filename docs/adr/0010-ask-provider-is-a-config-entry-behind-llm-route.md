# ADR-0010: The Ask/Explorer provider is a config entry behind the host's /llm route

Status: Accepted (2026-06-11 — shipped and field-proven the same evening: first live B70
inference through Ask and the Explorer, no code change to switch backends)

## Context

`docs/provider-contract.md` has long stated the rule — a provider is
`{ baseUrl, api: "ollama" | "openai" }`, configured outside the app — but the implementation
hardcoded Ollama three ways: the host's `/ollama/*` proxy targeted `127.0.0.1:11434`, the
Vite dev proxy sent `/ollama` straight to 11434 (bypassing the host entirely in dev), and
`provider.js` spoke only the Ollama wire protocol. The dual-B70 llama-server and the vllama
facade speak **OpenAI only** (`/v1/models`, `/v1/chat/completions` SSE) — verified against
`D:\work\vllama\src\Vllama\Facade\FacadeHost.cs` before building; the earlier assumption that
"pointing the proxy at :8090" would suffice was wrong, and would have shipped a protocol
mismatch.

## Decision

Three pieces, all behind the existing contract:

1. **Config carries one provider entry**: `LeopardConfig.AskProviderUrl` +
   `AskProviderApi` (`"ollama"` | `"openai"`); null/null = local Ollama on 11434, so existing
   installs change nothing. Same convention as `LiveInferenceUrl` (config-file only, no UI yet).
2. **The host exposes `/llm/{**path}`** — a streaming reverse proxy to the configured URL
   (one shared proxy body also serves the legacy fixed-target `/ollama` alias). The Vite dev
   proxy forwards `/llm` to the **host**, not to Ollama, so the configured provider applies
   identically in dev and packaged builds.
3. **`provider.js` speaks both protocols through `/llm`**, choosing by `askProviderApi` read
   from `/api/config`: Ollama NDJSON (`/api/tags`, `/api/chat`) or OpenAI SSE (`/v1/models`,
   `/v1/chat/completions`). The protocol parsers (`normalizeApi` / `parseModels` /
   `extractStreamToken` / `chatRequest`) are pure, exported, and vitest-covered.

Supporting decision: **`PUT /api/config` merges absent/null optional fields from disk.** The
Setup tab PUTs `{ logDir }` alone, and the optional fields are only ever *set* by hand-editing
config.json — so absence means "keep", never "clear" (clearing = edit the file). This also
fixed the pre-existing footgun where a Setup save silently wiped `liveInferenceUrl`.

## Consequences

- Running Ask/Explorer on the second B70 is a config edit, not a build:
  `{ "askProviderUrl": "http://127.0.0.1:8080", "askProviderApi": "openai" }`.
- Verified with a dual-protocol mock (`tools/mock-provider.mjs`): configured target honored,
  SSE streamed intact through the host, cleared config falls back to exactly
  `127.0.0.1:11434`, Setup-style saves preserve the entry.
- Field lessons from the same evening, fixed forward: provider detection must **retry while
  absent** (a model server often finishes loading after the app window opens); chat errors
  must surface the **provider's message** (a bare HTTP 400 hid llama-server's
  `exceed_context_size_error`); and an OpenAI-protocol llama-server enforces its `-c` context
  hard — the B70 launch script needs `-Context` sized for multi-slice contracts (32768 used).
- Cost accepted: one provider entry, not a provider *list* with a picker — the contract doc's
  full shape. The list + Setup UI is the upgrade path when a second simultaneous provider
  actually matters.
- Cost accepted: `loadedModels()` (warm-model detection) is Ollama-only; OpenAI-shaped
  providers report their `/v1/models` as servable, which is true by construction for
  llama-server.
