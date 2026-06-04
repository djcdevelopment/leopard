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
