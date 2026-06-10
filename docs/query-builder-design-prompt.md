# Query Builder Design Prompt

**Status:** Constraints locked — open questions teed up for the composer build session.  
**Sibling docs:** `docs/property-inventory.md` (the palette), `docs/shape-design-brief.md`, `docs/career-roster.md`

> We are building an **explainable retrieval engine**, not a chatbot. The user selects what they care about; those selections assemble a context the model answers *only* from; the user sees exactly what was assembled before asking. The query builder is the surface that makes this selection visible and composable. Internally it is called a "query builder" or "lens composer"; the user never sees either phrase.

---

## Primary product principle

**Before asking anything, the user already understands what Leopard knows — so they trust the answer before it is generated.**

This single idea carries three things at once:

1. **Engineering discipline** — `display == send`. The bytes the user reads in "What Leopard knows" are the bytes the model receives. No hidden context, no display-only decoration. This is now structural: `CanonicalContext.render()` is defined in terms of `serialize()` (`context.js`), so divergence is impossible to introduce accidentally.
2. **Grounding philosophy** — the model answers only from what was selected. Omissions are as meaningful as inclusions.
3. **UX goal** — the user *sees* the context assemble as they pick a lens. The demystification moment lands at comprehension, one step before Ask.

**Corollary — the lens framing:** the user's selections are the real product. The XML is an implementation detail. The user never hears "query builder", "profile", "context", "XML". They hear **lens**: `Raid data → pick a lens → "What Leopard knows" → Ask`.

---

## Locked constraints

These are non-negotiable architectural decisions. The composer build cannot change them without a design review.

### 1. Versioned envelope

Every emitted XML context carries a schema version on the root element from the first emit:

```xml
<context version="1" digest="sha256:5ef0d..." propertyCount="14">
  ...
</context>
```

**Why it's locked:** local models get trained against these prompts. An unversioned format silently breaks every saved query when the schema changes. This mirrors the existing `.trends.v2.json` / `.shape.v1.json` cache-version discipline (`Program.cs:45–67`). The `schemaVersion` field is already in `CanonicalContext` from day one (`context.js:SCHEMA_VERSION`).

**What it enables:** a saved lens carries its schema version; a version mismatch is detectable and can trigger a re-serialize rather than silently emitting stale structure.

### 2. Canonical serialization (the contract lives in code, not prose)

Identical selections must produce **byte-identical** XML. Three rules, implemented explicitly:

1. **Ordering** — sort property elements by stable ID, **ascending, ordinal, culture-invariant** (`StringComparer.Ordinal` / `localeCompare('en', { sensitivity: 'variant' })`). Never by label, never by insertion order, never by display priority.
2. **Fixed whitespace** — indentation and line endings are fixed constants in the serializer, independent of click order or platform.
3. **Non-recursive emission** — flatten the property graph into a list **before** serializing. The serializer is `list.map(toXmlElement).join('\n')` — no recursion over the graph. Future graph evolution cannot introduce accidental cycles into the serialization path.

**Why it's locked:** canonical serialization is what makes the context hashable, cacheable, reproducible, and regression-testable. It is the natural extension of `CanonicalContext.digest()` (already implemented in `context.js`). A stray sort-order dependency discovered in production means every answer from that build is unreproducible.

**The manifest:** each serialized context carries `sha256 + propertyCount + schemaVersion` as a compact fingerprint. A bug report can cite "context 5ef0d…" instead of an irreproducible screenshot.

### 3. Graph-first storage; XML is one serialization

Internally the context is always a **property graph** (`Boss ─ Pull ─ Player ─ Ability ─ Timeline`). XML / JSON / markdown are interchangeable *serializations* of a flattened projection of that graph. Committing to graph-internal now prevents document-shaped schema lock-in later.

**What this means for the composer:** the selection state is a set of `(propertyId, scopeParams)` tuples pointing into the graph. The serializer flattens those tuples into a list and emits XML. Switching from XML to JSON is a serializer swap, not a data model change.

**What this does NOT mean:** the graph does not need a graph database. A plain JS object `{ nodeId → node }` with edge arrays is a graph. Keep it simple.

### 4. Stable property IDs and saved lenses

A lens is a saved list of **stable property IDs** (+ scope params), e.g.:

```json
{
  "lensId": "healing-review-v1",
  "schemaVersion": "1",
  "properties": [
    { "id": "raid.pull.deaths@v1", "scope": { "encounterId": "*" } },
    { "id": "trend.coherence.followershipMean@v1", "scope": { "window": 6 } },
    { "id": "career.direction@v1", "scope": { "careerId": "fyrakk-heroic" } }
  ],
  "provenance": {
    "created": "2026-05-10T20:00:00Z",
    "lastEdited": "2026-06-01T18:30:00Z",
    "useCount": 12,
    "derivedFrom": null
  }
}
```

The XML is a pure function of the saved IDs + resolved data. Stable IDs (`@vN` — see `docs/property-inventory.md`) + the version envelope are what let a shared lens keep working as the schema evolves.

**Provenance is required on every lens:** `created`, `lastEdited`, `useCount`, `derivedFrom` (which lens/template it was forked from). This enables recommendation ("others in your situation use…") and lineage ("you forked this from the Healing Review template").

**The real product:** once users can trade lenses, the tool has become a **community analysis language**. A lens is the enduring artifact; Ask answers are ephemeral.

### 5. Provenance on every emitted property (explainable retrieval)

Every XML element carries its source metadata so the model can distinguish fact from interpretation:

```xml
<!-- exact fact -->
<duration source="career.json" artifact="career-input" exact="true" confidence="1.0">342000</duration>

<!-- derived label -->
<direction source="CareerSummary" artifact="career-summary" derived="true"
           derivedFrom="bossEndPctHp-series" confidence="0.72">improving</direction>
```

The inventory (`docs/property-inventory.md`) is the source table for `source`, `artifact`, `exact/derived`, `derivedFrom`, and `confidence` values.

### 6. Confidence is computable, not editorial

Derived property confidence **propagates mathematically** from `derivedFrom` inputs:

- Exact fact: `confidence = 1.0`
- Mean/count of exact values: `confidence = 1.0`
- Direction/trend label (categorical from noisy series): `confidence = min(pullCount / 10.0, 0.95)`
- Composite: `confidence = min(confidence of all inputs)`

**Why it must be computable:** if confidence is a hand-set tag, two builds can disagree. If it's computable, it's part of the deterministic serialization contract — two identical selections always produce the same confidence values. The formula is the spec; deviation from it is a bug.

### 7. `<UserIntent>` block — selections and explicit omissions

The context prepends an intent block that names both what was selected and what was explicitly not selected:

```xml
<UserIntent>
  <selected>deaths timeline, movement, interrupts, boss progress</selected>
  <explicitlyOmitted>damage dealt, healing output</explicitlyOmitted>
</UserIntent>
```

Negative information is information. If the analyst omits healing output, the model should interpret questions about healing output as "outside this lens" rather than "data not available". The `<UserIntent>` block is a pure function of the lens `properties` list vs the full property palette.

### 8. Continuous retrieval eval — treat prompt schemas like APIs

**Do not assume XML wins. Do not validate once.**

- Maintain a benchmark suite (target: hundreds of real questions with known-good answers). Every schema revision replays the suite → scores → approve or reject before shipping.
- The **format bake-off** is the first run: XML vs JSON vs markdown table vs bullet list on identical questions, *measured* against known-good answers, before the first format is locked. The format that maximizes answer quality on real questions wins — not the format that looks most structured.
- This is the same gate the `vllama`/local substrate runs through for model regressions.

---

## UX framing (locked — designers designing UX, not engineers)

The property inventory is the engine, never the entry point. The user travels:

```
intent → lens → "What Leopard knows" → Ask
```

### Discovery before precision

The first screen asks *"What are you trying to understand?"*:

```
○ Progression  ○ Healing  ○ Deaths  ○ Mechanics  ○ Movement  ○ Recruitment
```

These are **lenses** (named intent-driven templates). The checklist of `☐ raid.pull.durationMs@v1` is a *drill-down*, reached only by power users who want it. Never the front door.

### Curiosity, not checkboxes

Offer questions, not fields:

> *"Show me why people die"* → expands to ✓ death timeline, ✓ first deaths, ✓ nearby mechanics, ✓ movement

People think in questions. The property set is what the system assembles *behind* the question. The intent-named templates (🩹 Healing / ⚔️ Damage / 📈 Progression / 💀 Deaths / 🧠 Mechanics) are these curiosity entry points.

### Never show XML by default

The visible surface is "What Leopard knows" — a human-readable list of what's been assembled:

```
✓ Last 10 pulls on Fyrakk
✓ Boss HP trend across the career
✓ Deaths per pull (avg 11, peak 22)
✓ Followership: 0.701 (raid moving together)
```

Raw XML lives behind a power-user *"show the raw context"* disclosure. Never the default view.

### Context richness band, not a quality percentage

Do **not** surface "estimated answer quality: 87%". Users read that as truth and it implies false precision. Show a qualitative band:

```
Minimal · Focused · Balanced · Comprehensive
```

Context size informs the band; it is never shown as an accuracy score.

### The magic moment — watching "What Leopard knows" assemble

As the lens fills, the lines appear one at a time:

```
✓ Last 10 pulls on Fyrakk...
✓ Boss HP trend...
✓ Deaths per pull...
```

The user understands the context *before* asking. This is the demo. This is the demystification principle, made visible.

### A saved lens is a visual card, not a file

```
🩹 Healing Review
used yesterday · covers 18 metrics · shared by 42 guilds
```

Nobody thinks about `query.json`. The card is the product artifact; provenance metadata (last used, shared count, forked from) surfaces on the card.

### Language

| Surface | Say | Never say |
|---|---|---|
| UI copy | "Tell Leopard what matters" | "query builder", "profile", "context", "XML" |
| UI copy | "What should Leopard pay attention to?" | "select properties" |
| UI copy | "What Leopard knows" | "prompt context" |
| Engineering | query builder, lens, context, XML, stable ID | (all fine internally) |

---

## Open questions (for the composer build session)

### Adaptive context budget — semantic compression ladder

When the token budget shrinks, degrade gracefully rather than truncating arbitrarily:

```
fine metrics → aggregates → summary → titles only
```

**Unresolved:** which specific properties collapse to which aggregate, and at what token thresholds. Define the ladder rungs before the composer ships.

### Unit of selection in the UI

How does `DrillIn` (`PipelineTab.jsx:104–159`) become "add this property to my lens"? The `DrillIn` component already shows `does / sees / emits / sample` — the composer is that interaction + a ☑ control. **Unresolved:** the exact widget (checkbox? drag-to-lens? one-click add with undo?).

### Multi-scope lens declaration

Today's `scopeLabel` (`AskPanel.jsx`, now `context.scopeLabel`) assumes a single zoom. A composed lens mixing a night pull + a career stat has two scopes. **Unresolved:** how does the lens declare its combined scope? Options:
- A primary scope + secondary scopes
- A flat list of `(propertyId, scopeParams)` tuples (each carries its own scope)
- A scope graph node per selected property

The flat tuple approach is cleanest and fits the existing `CanonicalContext` model.

### Concrete XML element/attribute schema

The envelope is locked (`<context version="1">`). The per-property element/attribute schema is not:

```xml
<!-- Option A: attribute-heavy -->
<property id="raid.pull.deaths@v1" value="8" exact="true" confidence="1.0" source="career-input"/>

<!-- Option B: element-heavy -->
<property id="raid.pull.deaths@v1">
  <value>8</value>
  <provenance exact="true" confidence="1.0" source="career-input"/>
</property>
```

**Unresolved:** pending the format bake-off. Do not lock the per-property schema until retrieval eval scores are in.

---

## Integration point with existing code

### `CanonicalContext` is the seam

`ContextBuilder → freeze() → CanonicalContext` (`src/leopard-web/src/context.js`) is already the abstraction the composer plugs into. Today's three zooms produce a `CanonicalContext` from text blobs (night/career) or a structured enc (trend). The composer will produce one from the assembled XML:

```
lens selection  →  ContextBuilder.setXml(xml, scopeLabel)  →  CanonicalContext
                   ^-- new builder method, same interface
```

`buildMessages` and `chatStream` are unchanged — they already receive `context.serialize()`.

### `DrillIn` is the inspector half already shipped

`PipelineTab.jsx:104–159` shows `does / sees / emits / sample` for a pipeline stage. The composer extends this interaction model: "add this property to my lens" is one control added to the DrillIn pattern. Extend; do not start a new surface.

### The former zoom presets become intent-named lenses

`AskPanel`'s "This night / A boss's career / Recent form" buttons become the front-door lenses. They are **not removed** — they become the first three template lenses in the lens picker, each backed by the same `CanonicalContext` interface. A user who clicks "A boss's career" gets the career lens, pre-filled, editable.

---

## Deferred (not this session)

- **Structured box-score emit (C#)** — `BoxScore.cs` and `CareerSummary.cs` today produce markdown blobs. Field-level selection from night/career zooms requires a structured emit alongside the markdown. Needs the Tempo sibling repo and Windows toolchain. The inventory (`docs/property-inventory.md`) marks these fields and documents what the C# change looks like.
- **Composer vertical slice** — domain locked to a boss's career (cleanest structured substrate). Replaces zoom presets with intent-named lenses as the front door. Acceptance bar = the demystification moment (the user sees "What Leopard knows" assemble before asking; picking/dropping a property visibly changes it). Gets its own build plan once the decisions above are confirmed.
