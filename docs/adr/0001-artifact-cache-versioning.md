# ADR-0001: Version-suffix artifact cache keys on schema change

Status: Accepted (2026-06-07)

## Context

Each parsed night caches several artifacts (`.md` box score, `.trends.v2.json`, `.trace.json`,
`.career.json`, `.shape.v1.json`) keyed by the source log's mtime. The parse step regenerates an
artifact only when its cache file is **missing** (`Program.cs`: "regenerate if ANY is missing").
mtime handles content freshness, but it does **not** handle *schema* freshness: when an artifact's
JSON shape changes (e.g. Trends single-window -> selectable 4/6/8/10 windows), re-parsing an
unchanged log is a silent no-op — the old-shape cache still exists, so nothing regenerates and the
UI reads stale structure.

## Decision

Encode a schema version in the cache filename suffix (`.trends.v2.json`, `.shape.v1.json`). Bumping
the version makes prior-shape artifacts read as **absent**, so the existing "regenerate if missing"
path rebuilds them on the next parse. The `/api` reader and the parse-time writer reference the same
path helper, so they stay in lockstep.

## Consequences

- A plain re-parse reliably upgrades artifacts after a schema change — no cache-wipe tooling.
- Old `vN` files are orphaned but harmless (cleanable later).
- Every schema change MUST remember to bump the suffix; this ADR is the reminder.
- A corpus-wide re-parse is still required to backfill the new shape across already-parsed nights
  (e.g. the 74-night Shape backfill on 2026-06-07).
