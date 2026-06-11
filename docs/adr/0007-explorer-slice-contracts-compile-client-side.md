# ADR-0007: Explorer slice contracts compile client-side over cached artifacts

Status: Proposed (2026-06-11)

## Context

The Explorer (the knowledge-library IDE, eighth surface) lets the user assemble a *contract*
of knowledge objects — the ported RaidUI math modules — and compile it into the versioned XML
context the model receives. Something has to own that compilation: turning "Pulse + Reaction
spread + Pull diff, with these slice settings" into deterministic, digest-signed bytes.

Three placement options:

1. **Server-side context compiler** — a new `/api/context/compile` endpoint in leopard-host
   that reads the cached artifacts and emits the XML.
2. **Client-side pure-JS compiler** — a `contract.js` module in leopard-web that compiles
   from the artifact payloads the browser already fetched, following the precedent `lens.js`
   set for the career lens.
3. **Extend `lens.js` in place** — grow the career-lens composer into a general slice
   compiler.

## Decision

**Client-side, as new pure modules** (option 2): `knowledge.js` (the object registry — id,
category, live/ghost status, provenance metadata, slice options) and `contract.js` (the
compiler — `buildContract` → `{xml, sliceItems, totalTok, digest}`). `lens.js` and
`context.js` are **untouched**; the new compiler imports `sha256hex` and copies the
established canonicalization rules (escape discipline, sort-by-stable-id ordering, digest
over the inner XML with the root excluded) rather than modifying their owner.

Supporting decisions bundled here:

- **The compiled XML feeds `ContextBuilder.setText(...)`** so the display==send invariant
  (ADR-0004) stays structural — the editor pane shows exactly the bytes the model receives.
  The mockup's self-closing `<slice/>` look is a *display-only* folding transform; the sent
  bytes are always the full payload.
- **The registry carries the ghost taxonomy.** Every knowledge object from the design mockup
  exists in `knowledge.js` from day one with `status: 'ghost'` and `api: null`; phase 2 flips
  entries to `live` and adds a fetcher without reshaping the tree.
- **Slice attributes are real where cheap, annotations otherwise.** Signals `rep`/`agg`/`time`
  and players `scope` genuinely reshape the serialized payload; attribute combinations the
  compiler does not branch on yet are still recorded in the XML and flagged in the UI as
  annotation-only — never silently pretended.
- **`tok=` values are the chars/4 heuristic**, documented as such. The contract needs relative
  weight for composition decisions, not billing-grade counts.

## Consequences

- Phase 1 shipped with **zero host changes** for the compile path — the four live objects ride
  the artifact endpoints that already existed (`/api/signals|players|affinity|diff`).
- The artifact payloads the tab fetches once per night double as availability probes (404 ⇒
  amber "re-parse in Setup" dot), preview data, and slice substrate — no duplicate requests.
- The compiler is pure and vitest-covered (golden XML, digest stability, attr-change ⇒
  digest-change, canonical ordering) — 19 new tests alongside the existing 18.
- Cost accepted: the digest signs *client-compiled* bytes; there is no server attestation that
  a given digest corresponds to a given artifact vintage. If contract digests ever become
  load-bearing records (e.g. in the live-insight jsonl), a host-side recompile-and-verify is
  the upgrade path.
- Cost accepted: registry metadata (stage, truth, stream, confidence) is hand-maintained JS.
  If it drifts from the C# artifact reality, the Properties panel lies. A host-served object
  catalog generated from the artifact builders is the eventual fix; hand-curated is fine at
  16 entries.
