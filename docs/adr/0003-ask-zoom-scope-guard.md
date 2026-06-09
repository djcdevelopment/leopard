# ADR-0003: Ask guards scope, not just numbers

Status: Accepted (2026-06-07)

## Context

Ask (the Leopard tab) was widened from a single night box score to a zoom family — night, career
arc, recent form — in the zoom-grounding stretch (`b042a9c`–`d50188b`). The naive widening would
add new zoom branches and leave the grounding prompt unchanged: a user asking "are we getting
better?" while zoomed to a single night would receive the night's slope presented as the whole arc.

That is a grounding failure — not a numbers invention (the old failure mode) but a *scope
overreach*: confidently answering a question the evidence set cannot support.

## Decision

`prompt.js` was rewritten to carry `scopeLabel` as an explicit parameter. The system prompt
declares the scope of its evidence set and instructs the model to **decline and redirect**
cross-scope questions rather than answer them with insufficient data.

Example: night-zoomed Ask, asked "are we improving over time?", now responds: *"I can see tonight's
data but not your full progression arc — check the Career zoom or Trends for that."*

The redirect is treated as a feature, not a fallback. The scope guard was verified to actually
fire: a night-scoped Ask was tested with a cross-night trend question and confirmed it redirected
rather than hallucinating a slope.

## Consequences

- Grounding now means *scope*, not just *numbers*. Both failure modes have explicit guards.
- A zoom that offers career data must also carry a `scopeLabel` that names its horizon; the prompt
  contract requires it.
- The valuable test is "does the guard *fire*" — not "does the happy path work." Testing only the
  in-scope case leaves the scope-guard untested.
- Decision driver: "refuse is a feature — test that the guard fires." See
  `retros/ask-zoom-grounding-2026-06-07.md`.
