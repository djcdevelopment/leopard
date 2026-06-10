# Retro: CanonicalContext + career lens — the query-builder ensure and compose rungs

*The display==send invariant moved from conventional to structural, a vitest harness guards it with
18 tests, and the career zoom's "What Leopard knows" panel lets the user compose the model's
evidence before asking. The query builder's first vertical slice shipped.*
*Date: 2026-06-09 · Scope: d50188b..HEAD + unstaged compose work (lens.js, AskPanel integration)*

---

## What shipped

Two discrete rungs of the query-builder PDLC, separated by a session boundary.

**Ensure rung** (commit `7e1d0d5`, 2026-06-07): `CanonicalContext` — a frozen value object
produced by `ContextBuilder → freeze()`. The load-bearing structural rule: `render()` is
**defined in terms of** `serialize()`, sharing the same underlying bytes. Display cannot drift from
payload because there is no separate display path. The object carries `schemaVersion`, `scopeLabel`,
and `digest{sha256hex, propertyCount, schemaVersion}` from day one; SHA-256 is inline pure JS (no
deps). `trendSummaryText` was migrated from `AskPanel` into `ContextBuilder.setTrend()` — one
canonical path.

The vitest harness (`context.test.js`, 18 tests) guards the guarantee: `render()===serialize()`
byte-for-byte on all three zoom shapes, determinism (same inputs → same SHA-256), frozen-value
mutation rejection, and SHA-256 cross-checked against Node `crypto.createHash` including the RFC
empty-string vector. Red-check confirmed: a stray character in `render()` breaks 4 tests.

Alongside: `docs/property-inventory.md` (249 lines — all 7 artifact types, 9+ career properties
with stable versioned IDs `roster.attempts@v1` … `roster.lastSeen@v1`, every field traced to its
C# source line, disk-to-confirm items flagged) and `docs/query-builder-design-prompt.md` (290
lines — locked constraints: versioned envelope, canonical serialization, graph-first storage, lens
framing, provenance on every emitted property, computable confidence, UserIntent block, format
bake-off, open questions for the composer build).

**Compose rung v1** (unstaged, 2026-06-09): `src/leopard-web/src/lens.js` (+119 lines) — a pure
function `buildCareerLens(boss, selectedIds) → {xml, displayItems, propertyCount}` over the
`CAREER_PALETTE` (9 properties: attempts, kills, killed, bestPct, direction, arc, totalTimeMs,
firstSeen, lastSeen). Each property carries a stable versioned ID, provenance (`exact=true` vs
`derived=true derivedFrom="..."`) and confidence (static 1.0 or computed `min(attempts/10, 0.95)`
for `direction`). XML output is a versioned envelope with a `UserIntent` block declaring the user's
selected and explicitly omitted properties, then the property elements, then a SHA-256 digest of
the inner content.

`AskPanel.jsx` integration: the career zoom now renders a **"Tell Leopard what matters"** checkbox
palette (all 9 properties, default = everything except total time). The evidence panel becomes
**"What Leopard knows"** — a human-readable list assembled live as properties are toggled, with a
"Show raw context (XML sent to model)" disclosure triangle for the full payload. The demystification
moment: the user watches the evidence list build, then asks. `context.js` exported `sha256hex` so
`lens.js` can use the same hash function. A zoom auto-reset on night change was added: if the user
is on career zoom and picks a new night, the zoom snaps back to night (career data is all-time,
not night-scoped — staying on it when the night changes feels stale).

Smaller item: `Program.cs` dev/prod URL routing fix — the desktop window now routes
`Development` environment to Vite (:5273) and `Production` to the host bundle (:5280). Previously
both environments used :5280, so HMR in the desktop window never worked.

---

## Engineering Lead perspective

The `CanonicalContext` design decision is the cleanest invariant-enforcement move in the
codebase so far. The previous display==send guarantee was developer discipline: three separate
branches each constructing an evidence string and a display render, held together by the convention
"use the same string in both places." That works once; it degrades as paths multiply. The
structural fix — `render()` is not a parallel implementation of `serialize()`, it IS `serialize()`
rendered — means the invariant is in the type shape, not the developer's attention.

`lens.js` is factored correctly: a pure function with no React, no side effects, no state. Input:
a structured boss row (already in the Roster) and a `Set<string>` of selected property IDs. Output:
`{xml, displayItems, propertyCount}`. The `displayItems` array feeds the "What Leopard knows" list;
`xml` goes into `ContextBuilder.setText()`. They are derived from the same `(boss, selectedIds)`
pair in the same deterministic pass — another structural guarantee, not a convention. The function
is directly unit-testable without a DOM or React.

The `sha256hex` export from `context.js` is a small but correct decision. lens.js needs to compute
the same digest as `CanonicalContext` would; sharing the function means the hash is the same
implementation, not just the same algorithm name. Diverging implementations of SHA-256 would
produce the same correct hash for valid inputs but different hashes for edge cases and erroneous
inputs — sharing the function eliminates that divergence class.

Debt status: the vitest harness closes the gap the ask-zoom-grounding retro called out — the
display==send invariant is now guarded. New gap opened: the lens composer UI integration (does
toggling a checkbox actually produce a different XML in the evidence panel, and is that XML the
bytes the model receives?) has no automated test yet. The function is testable; the test doesn't
exist yet. The IPv4 proxy smoke remains a manual check. The dead v1-shape fallback in `TrendsTab`
is still there. Draft PR (`#1`) has three Windows-side checks outstanding before merge.

---

## Project / Program Manager perspective

The ensure→verify→compose PDLC discipline held exactly as designed. `CanonicalContext` came first
(ensure: lock the invariant). The vitest harness proved it (verify: guard the invariant
structurally). `lens.js` was built on the locked seam (compose: add capability against the
invariant, not around it). The session-pickup doc (`docs/session-pickup-2026-06-07.md`, now
committed) was the navigation surface that allowed the compose rung to start without context-rebuild
— it named the acceptance bar, the seam to use, and the deferred items explicitly.

Acceptance bar from the session-pickup doc: *"the demystification moment — the user sees 'What
Leopard knows' assemble before asking."* That bar was met. The checkboxes, the assembling list, and
the disclosure-triangle raw context are all in the AskPanel diff.

Deferred with explicit reason: night and trend lens builders require structured C# emit alongside
the current markdown blobs. `BoxScore.cs` and the trends API return markdown; field-level selection
from those zooms requires a parallel structured path from C#. This was documented in
`docs/property-inventory.md` with source lines. It's not a decision to defer later — it's a known
dependency that gates those lenses. The career lens was the right first slice because `.career.json`
is already field-addressable structured JSON.

Draft PR #1 remains open on `inventory-canonical-context`. The three Windows-side checks
(dotnet build for the updated Vite hash, dotnet test for C# regression, disk-to-confirm fields in
the property inventory) are blocking merge and require Derek on the Windows machine with the
toolchain. These are trivially closable; they have been listed since the ensure-rung session.

The push accumulation pattern continues: the branch is several commits ahead of origin. The ask-
zoom-grounding retro (two days old) was untracked until this retro commit. Retros that live in the
working tree are invisible to the next session's `/sup` scope discovery.

---

## QA / Verification perspective

The vitest gate is the durable QA win of this stretch. 18 tests covering exactly the invariant that
was previously a code-reading exercise. The test methodology is sound: the red-check (a stray
character injected into `render()` breaks 4 tests) confirms the harness catches what it was
designed to catch, not just what happens to pass. SHA-256 cross-validated against Node `crypto`
including the RFC empty-string vector — the hash function is not just self-consistent, it's
validated against an external reference.

The lens.js pure-function factoring is a testing gift: it can be imported and exercised directly
in a vitest context without a DOM, a provider, or a mounted component. The test that should exist:
`buildCareerLens(fixture_boss, CAREER_DEFAULT_IDS)` → check that `xml` contains exactly the
expected property elements in stable sorted order, that `displayItems.length === propertyCount`,
that omitting `roster.totalTimeMs@v1` removes it from both xml and displayItems, and that the
digest in the envelope is SHA-256 of the inner content (computable by test). This test does not
exist yet and should be the first addition to the vitest suite in the compose session.

The zoom auto-reset on night change is a behavioral correctness fix, not a visual polish item:
career data is all-time and does not change when a different night is selected, so staying on career
zoom after a night switch shows stale-feeling data from a different context. The fix is in the
`prevNightRef` / `useEffect` pattern in `AskPanel`. Manual verification: pick career zoom on boss A,
change the night, observe the zoom snaps to night. No automated guard — behavioral UX, manual check.

Still uncovered: lens composer UI integration (checkbox → XML → model bytes), IPv4 proxy smoke
(needs live Ollama). The three Windows-side checks for PR merge are gate items, not just debt.

---

## Operator perspective

*(first person — Derek)*

This stretch delivered the moment I've been building toward since the Roster shipped: the user
seeing *exactly what the model knows* and having the power to change it. "Tell Leopard what matters"
— checkboxes that directly update the XML the model will read. The "What Leopard knows" list
assembles live. Before this, grounding was a promise. Now it's legible.

The `CanonicalContext` decision is one of those quiet architectural wins that won't get credit but
will prevent a whole class of bugs. We weren't going to violate display==send intentionally — but
as more lenses get added, a convention is just a test that doesn't exist yet. Making render() be
serialize() removes the class.

The dev/prod URL fix (`Program.cs`) is the kind of thing that was always slightly annoying in the
background — HMR in the desktop window never worked because the window always pointed at :5280
regardless of the environment. One-line fix; should have been caught earlier. The kind of thing
that slides for weeks in a solo project.

The session-pickup doc was the artifact that made the compose rung frictionless. I wrote it before
the ensure-rung session ended and it contained exactly what the next session needed: the seam to
use, the acceptance bar, the domain for the first slice, what NOT to build yet. That's a pattern
I want to repeat.

What I'm being honest with myself about: the draft PR has been open since the ensure-rung session
and the three Windows-side checks have been on a list for two days. They're trivial. I just haven't
done them. The push accumulation and the untracked retro are the same pattern — solo project,
no external pressure to merge. The retros being untracked is worse than the push lag because it
means the next session's `/sup` sees an incomplete picture of where we left off.

---

## How we worked together (human ↔ AI)

### What worked well

- **The session-pickup doc as the conversation bridge.** The compose rung was built from a cold
  start against a session-pickup doc the previous session wrote. The doc named the acceptance bar
  ("demystification moment"), the seam to use (`ContextBuilder.setText()`), the domain (career
  lens, not night or trend), and the explicit deferral (structured C# emit gates the other lenses).
  The session started at the compose step, not at "figure out what was built before." This is the
  pattern that makes multi-session work not feel like restarting each time.

- **Pure function factoring from the locked constraints doc.** `lens.js` came out correctly
  structured (pure, no React, no side effects) because the `docs/query-builder-design-prompt.md`
  had already locked the constraints: lens framing, provenance on every property, UserIntent block,
  canonical serialization. The builder read the constraints doc and produced code that matched the
  contract without re-deriving it mid-session.

- **The structural guarantee over the conventional one.** The AI and operator both landed on
  `render()` IS `serialize()` as the right fix rather than "test that they're equal." Tests that
  compare two independently-computed outputs can drift if both implementations drift together.
  Making them the same code path removes the drift class entirely.

- **Vitest red-check confirmation.** The harness was validated not just "these tests pass" but
  "injecting a known fault breaks the tests we'd expect." That confirmation pattern (verify the
  guard fires) is consistent with how the scope-guard ADR was framed: the valuable check is that
  the guard fires, not that the happy path passes.

### What didn't

- **The ask-zoom-grounding retro was untracked for two days.** A retro that lives in the working
  tree is invisible to the next session's `/sup` ritual — which reads committed retros to scope
  what's happened. This one was sitting next to the work it describes but invisible to any tooling
  that reads git history. If the compose session had started with `/sup`, it would have seen the
  Shape retro as the most recent committed retro and missed the two sessions of work between them.

- **Draft PR #1 has been open since the ensure session with three blocking checks unresolved.**
  They are trivial (dotnet build, dotnet test, three field confirmations). The pattern is: a cloud
  session ends with Windows-side checks deferred to the operator, the operator doesn't close them,
  the branch accumulates more work on top of unresolved gate items. The checks are cheap; the
  accumulation cost is not.

- **The compose rung has no test for the lens pure function.** `lens.js` is testable — it's a pure
  function over a structured input. The test was identified, described in detail, and not written.
  The pattern "describe the test that should exist and defer it" has appeared in three consecutive
  retros now. At this point the lens test is the first item in the next session's acceptance bar,
  not a recommendation.

- **The career zoom's auto-reset and the palette UI had no explicit acceptance criteria before
  build.** They were both the right call — but "right call discovered during build" is a different
  confidence level than "gate item verified before merge." The auto-reset in particular is a
  behavioral correctness item (not cosmetic), and it currently rests on a manual check.

### Patterns to repeat

- Write the session-pickup doc before ending a cloud session. Name the seam, the acceptance bar,
  the domain for the next slice, and the explicit deferral list. It is worth 10x more than an
  equivalent amount of time spent on further feature work.
- Lock constraints in a design doc before building. The `query-builder-design-prompt.md` was the
  spec; `lens.js` implemented it. The builder didn't need to invent the provenance model.
- Commit design docs and retros immediately, even at draft stage. A retro that isn't committed is
  invisible to the next session's scope discovery.

### Patterns to change

- **Close Windows-side gate items the same session they're opened.** The three checks for PR #1
  are trivial and have been open for two days. If a gate item requires the operator's machine, the
  operator should do it before starting the next cloud session on the same branch.
- **Write the lens test in the compose session, not the next one.** The pattern "describe the test
  that should exist" has appeared in three retros. Once a test is described concretely enough to
  write in a retro, it is described concretely enough to write now.
- **Track in-flight retros in the MEMORY index.** If a retro is written but not committed, note
  it in the memory file so the next session's `/sup` knows to look in the working tree.

---

## Lessons learned

1. **A structural guarantee is worth more than a conventional one at exactly the moment when
   paths multiply.** display==send was safe with three zoom branches and one developer. It would
   not have stayed safe as the query builder added five more lenses. `render()` IS `serialize()`
   eliminates the class, not just the current instance.

2. **Provenance belongs on every emitted property from day one.** `exact`, `derived`, `derivedFrom`,
   `confidence` — these attributes on the XML properties are not metadata for later. They are the
   product's grounding claim at the property level. Retrofitting them after the career zoom, the
   night zoom, and the trend zoom have shipped would be a seam-ripping exercise.

3. **A pure function with stable property IDs is the right lens interface.** `(boss, selectedIds)
   → {xml, displayItems, propertyCount}` — no React, no state, no side effects. The display list
   and the XML are co-produced from the same pass over the selected properties. Testable, composable,
   and impossible to drift. The night and trend lenses will follow this shape.

4. **The session-pickup doc is a force multiplier on multi-session work.** The ensure session and
   the compose session were separated by a session boundary and potentially by days of context loss.
   The pickup doc made the compose session start at full speed. A retro documents what happened;
   a pickup doc enables what happens next. Both matter; the pickup doc has a shorter shelf life and
   higher leverage.

5. **"The test is described" is not the same as "the test exists."** Three consecutive retros have
   named the next test to write with sufficient specificity to write it. Writing a clear test
   description in a retro is useful context; not writing the test is still debt.

---

## Next moves

- **Close PR #1 gate items** (Derek, Windows machine): `dotnet build src/leopard-host` (Vite hash
  change), `dotnet test src/leopard-host.Tests` (confirm 9/9), disk-to-confirm fields in
  `docs/property-inventory.md`. Then merge the PR.
- **Write the lens test** — `buildCareerLens(fixture, CAREER_DEFAULT_IDS)`: check XML property
  elements, sorted order, displayItems.length === propertyCount, omission removes from both, digest
  is SHA-256 of inner content. Pure function — directly testable in vitest with no DOM.
- **Night and trend lens builders** (night-lens.js, trend-lens.js) — gate: structured C# emit from
  `BoxScore.cs` / trends API alongside the current markdown blobs. Documented in
  `docs/property-inventory.md`. This is the next vertical slice after the lens test.
- **Iterate the Rack prompt** (`docs/rack-design-prompt.md`) — answer section 5's gating questions,
  especially: where on the (a)→(b)→(c) ladder is v1, and how far with zero Tempo changes. This
  decides the build timeline.
- **ADR-0003 + ADR-0004** — landed in this retro commit. No further action needed.

---

## Acceptance gates met

- [x] CanonicalContext value object: render() defined in terms of serialize(), display==send structural (`7e1d0d5`)
- [x] vitest harness: 18 tests, render===serialize byte-exact on all 3 zooms, determinism, frozen rejection, SHA-256 vs Node crypto (`7e1d0d5`)
- [x] Property inventory: all 7 artifacts, stable versioned IDs, provenance, disk-to-confirm flagged (`7e1d0d5`)
- [x] Query-builder design prompt: locked constraints documented (`7e1d0d5`)
- [x] Career lens v1: CAREER_PALETTE (9 properties), buildCareerLens(), provenance + confidence, versioned XML envelope with UserIntent (lens.js, unstaged)
- [x] AskPanel integration: "Tell Leopard what matters" palette, "What Leopard knows" panel, zoom auto-reset on night change (unstaged)
- [x] Program.cs dev/prod URL routing fix — desktop window HMR now works in Development (unstaged)
- [x] ADR-0003 (Ask scope guard) + ADR-0004 (CanonicalContext display==send) — landed in this retro commit
- [x] ask-zoom-grounding retro, rack-design-prompt, session-pickup doc committed (this commit)
- [ ] Windows-side gate items for PR #1: dotnet build, dotnet test, disk-to-confirm fields
- [ ] lens.js pure-function test (buildCareerLens) in vitest suite
- [ ] Night and trend lens builders (gate: structured C# emit)
