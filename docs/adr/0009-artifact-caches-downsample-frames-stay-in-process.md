# ADR-0009: Per-night artifact caches carry chart-resolution series; frame-resolution stays in-process

Status: Accepted (2026-06-11)

## Context

Phase 2 of the Explorer surfaced three more ported modules through per-night artifact caches
(`.coverage.v1.json`, `.segments.v1.json`, `.classify.v1.json`). The coverage quality model
(`CoverageTimeline.Compute`) produces *per-frame* output — 200 ms resolution with a per-healer
breakdown per frame. A raid night of full-frame coverage JSON would run megabytes per night
and carry detail no consumer renders: the Explorer draws a line chart and serializes a prompt
slice; both want seconds, not frames.

But one consumer genuinely needs frames: the wipe classifier's coverage-pattern tags and
named-healer offender attribution read per-frame centrality/edge-proximity (the v2c design).

## Decision

**Caches store what the UI consumes; full resolution is recomputed in-process where needed.**

1. `CoverageTimeline.ToSeconds` downsamples the frame series to per-second averages
   (raid/tank/flex %, quality score) — that plus the summary (snaps included) is the entire
   cached payload. The per-frame DTOs never serialize.
2. `ClassifyArtifact.BuildJson` calls `CoverageTimeline.Compute(replay)` itself at parse time
   and hands the full frame-resolution series to `WipeClassifier.Classify` — the same pattern
   `LiveSession` already uses. The two artifact builders each walk the parse independently
   (the established BuildJson convention); the duplicate coverage compute per pull is seconds
   of parse-time cost, paid once per night.
3. The classify cache stores **verdict-or-explicit-reason, never silence**: every pull carries
   either a `classification` or a stated `reason` ("kill - kills are not classified",
   "no replay frames", "below the classifier's gates"). This extends the contract.js
   explicit-absence discipline (a model told what is NOT known) down into the artifact layer.

## Consequences

- The real 2026-06-09 night's coverage cache is 24 KB (vs megabytes at frame resolution);
  segments 12.7 KB; classify 1.2 KB. Fetch-per-night stays cheap for the Explorer's
  seven-artifact fan-out.
- Any future consumer that needs frame-resolution coverage (e.g. a scrubbing timeline viz)
  must recompute from the replay — acceptable: `Compute` is fast and the replay is already
  in the parse. If recompute cost ever matters, a `.coverage-frames.v1.json` sibling cache is
  the additive upgrade; the v1 schema doesn't change.
- The seconds-vs-frames split is per-artifact judgment, not a blanket rule: segments and
  classify are naturally small and cache their full output.
