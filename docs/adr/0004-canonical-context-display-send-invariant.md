# ADR-0004: CanonicalContext makes display==send structural, not conventional

Status: Accepted (2026-06-07)

## Context

Leopard's central grounding claim is: *the model reads exactly the evidence the user sees.* Before
`CanonicalContext`, this was a conventional guarantee — three separate zoom branches each built an
evidence string and a display render. If any branch diverged (different formatting, a missing
field, a truncation), it would silently ship. The invariant was verified by reading the code, not
by a guard.

As the query builder adds more lenses (career, night, trend, eventually user-defined), the number
of paths that must hold the invariant grows. A conventional guarantee degrades as paths multiply.

## Decision

Replace the three per-zoom evidence strings with `CanonicalContext` — a frozen value object
(`Object.freeze()`) produced by `ContextBuilder → freeze()`. The key structural rule:

> **`render()` is defined in terms of `serialize()`**: `render()` returns a React element whose
> text content is `this.serialize()`. They share the same bytes — there is no second path where
> display diverges from payload.

The object carries `schemaVersion`, `scopeLabel`, and `digest{sha256hex, propertyCount,
schemaVersion}` from day one. SHA-256 is computed inline (pure JS, no deps, browser + Node
compatible). `trendSummaryText` was migrated from `AskPanel` into `ContextBuilder.setTrend()` so
there is one canonical path for both display and the model payload on the trend zoom.

18 vitest tests guard the guarantee: `render()===serialize()` byte-for-byte on all three zoom
shapes; determinism (same inputs → same SHA-256); frozen value rejects mutation; SHA-256 validated
against Node `crypto.createHash` including the known RFC empty-string vector.
Red-check confirmed: a stray character injected into `render()` fails 4 tests.

## Consequences

- display==send cannot silently drift. Adding a new lens requires producing a `CanonicalContext`
  from a single evidence source — there is no separate display path to forget to update.
- `buildMessages` receives `context.serialize()` and `context.scopeLabel`. It never sees the
  display layer.
- The vitest harness (`src/leopard-web/src/context.test.js`) is the regression gate for this
  invariant — it replaces the "read the code to verify" process.
- The query-builder lens pattern (`lens.js → buildCareerLens → {xml, displayItems}`) was designed
  against this seam: `displayItems` is what the user sees; `xml` is what `ContextBuilder.setText()`
  receives. Same source data, separate render paths — `CanonicalContext` binds them.
- Decision driver: "the most capable feature is a rendering decision when the data layer is right."
  See `retros/canonical-context-and-career-lens-2026-06-09.md`.
