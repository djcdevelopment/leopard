# ADR-0002: Shape's kill-vs-wipe contrast is career-scoped, not per-night

Status: Accepted (2026-06-07)

## Context

Shape v1 pairs a per-pull density heatmap with a kill-vs-wipe contrast (`wkdelta`: avg deaths /
duration / peak deaths / best progress, kill column vs wipe column). The other per-night surfaces
(Trends, the density heatmap itself) compute from a single night's `ParseResult` and cache per
night. The obvious default was to compute wkdelta the same way.

A pre-build data check (`GET /api/career` over the 74-night corpus) falsified that default:
**on any single real night a boss is almost always all-kills or all-wipes** (a clear/M+ boss is
killed once; a progression boss is wiped on all night). Per-night, the contrast would have one
column empty every time. Across careers, **48 of 119 bosses have both** outcomes — Crown of the
Cosmos at 2 kills / 52 wipes.

`ShapeProjection.TryBuildWkDelta` resolves via `CareerProjection.TryResolveCareerEncounter`, which
groups by `careerId` over whatever encounter list it is handed — it does no I/O.

## Decision

Scope wkdelta to the **career** (fanned across every parsed night), not the night. Density stays
per-night and caches in `.shape.v1.json`; wkdelta is computed **live** at `/api/shape/wkdelta` from
the fanned per-night career-input artifacts (the same fan-in `/api/career` performs), keyed by
`careerId`.

## Consequences

- The contrast is populated and meaningful on real data instead of empty on every night.
- wkdelta is NOT in the per-night cached artifact — it is recomputed per request (cheap: small
  per-night JSON), and it always reflects the latest parsed corpus.
- The Shape tab's boss selector (per-night) must carry `careerId` to fetch the cross-night contrast.
- The "Best progress" kill column is `0` by definition (a kill is 0% boss HP), not a measured mean —
  the UI labels it accordingly and hides the whole kill column when `KillCount == 0` (the one-sided
  frame).
- Decision driver: validate against the data before building. See `retros/shape-shipped-2026-06-07.md`.
