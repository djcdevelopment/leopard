# ADR-0006: Live insight loop — three-repo division, jsonl file bridge, named feedback axes

Status: Accepted (2026-06-09)

## Context

The live loop (a pull ends → a grounded insight is ready before the operator looks) spans three
concerns: tailing a combat log that WoW is actively writing, turning facts into a grounded
insight, and painting it over the game. An earlier cloud-session draft put all three in Leopard:
a 512KB regex tail, five scalars as evidence, hardcoded Ollama `:11434/api/chat`, and
unstructured thumbs feedback. It never escaped its container, and its evidence was too starved
to grade — every 👎 would have measured input starvation, not model quality.

## Decision

Split the loop along seams the three repos already drew, with a file as the only coupling:

- **Tempo owns log → facts.** `LiveSession` consumes `Tempo.Core.Ingest.FileSystemLogMonitor`
  + `CombatLogParser` in-process (already a ProjectReference) — the flush-aware, rotation-safe
  tail problem was already solved and tested there. Zero Tempo changes; Leopard is the first
  consumer of the lane Tempo's ADR-018 calls "the post-wipe directive path."
- **Leopard owns facts → grounded insight → record.** Three-layer evidence (the pull's typed
  facts + tonight's in-memory trajectory + the cached all-time career), POSTed to a config
  `liveInferenceUrl` (OpenAI-style, default llama-server `:8080` — not hardcoded Ollama).
  Pre-generated, never awaited; a new pull cancels in-flight generation.
- **discoverlay owns delivery**, coupled only by tailing `%LOCALAPPDATA%\Leopard\live-insight.jsonl`.

Every lifecycle event appends one JSON line to that file: pull facts, insight pending/ready/error
(with the **exact** evidence, system prompt, params, and output), and feedback. Feedback is two
**named axes** — *useful* (worth reading between pulls?) and *grounded* (did it stick to the
evidence?) — orthogonal failure modes recorded separately, plus a free comment.

## Consequences

- The jsonl is simultaneously the overlay's feed and the critic-loop's eval corpus: every judged
  insight is fully replayable (evidence + prompt + params + output joined to feedback by
  `insightId`). The corpus is the point; the card is the collection device.
- "Who is the brain" is cheap to revise: if Tempo.Host's directive path matures into the live
  brain, it takes over writing the jsonl and nothing downstream changes.
- display==send holds in the live lane: the card's evidence disclosure shows the byte-identical
  block the model read, same as every Ask surface (ADR-0004).
- Live facts are staging, not canon: the canonical artifacts still come from PARSE after the
  night; the record's join keys reconcile them.
- Failure is a feature: inference down ⇒ the card says so honestly and the next pull retries —
  a raid night must never care whether the insight rig is up.
- The accepted risk: a file bridge has no backpressure or schema negotiation — the `v` field on
  every line is the versioning seam if the contract needs to move.
