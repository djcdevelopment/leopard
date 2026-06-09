# Pick-up-here — 2026-06-07 session

**Written for:** next Claude Code session after Derek's reboot  
**Branch:** `inventory-canonical-context`  
**Draft PR:** https://github.com/djcdevelopment/leopard/pull/1  
**Status:** all web deliverables shipped and green; two Windows-side checks outstanding before merge

---

## What was built this session

### Goal
The **"ensure" rung** of the query-builder PDLC: inventory what's selectable, lock the `display==send` invariant structurally, document the locked constraints so the composer build can start without stepping on landmines.

### Three deliverables, all on the branch

**1. `CanonicalContext` + vitest harness**

- **`src/leopard-web/src/context.js`** — new file. `ContextBuilder → freeze() → CanonicalContext`. The key structural guarantee: `render()` is *defined in terms of* `serialize()`, so display==send cannot drift. Carries `schemaVersion`, `scopeLabel`, `digest({ sha256, propertyCount, schemaVersion })` from day one. Inline sync SHA-256 (no deps). `trendSummaryText` migrated here from `AskPanel` — one canonical path for both display and the model payload.
- **`src/leopard-web/src/AskPanel.jsx`** — refactored. `evidence` string + `scopeLabel` state replaced by `context: CanonicalContext | null`. `buildMessages` now receives `context.serialize()` and `context.scopeLabel`. `trendSummaryText` removed.
- **`src/leopard-web/src/context.test.js`** — 18 tests. render===serialize byte-for-byte on all 3 zooms; determinism; frozen value rejects mutation; SHA-256 validated against Node `crypto.createHash` including known empty-string RFC vector.
- **`src/leopard-web/src/setupTests.js`** — jest-dom setup for vitest.
- **`src/leopard-web/package.json` / `vite.config.js`** — vitest, jsdom, @testing-library/react, @testing-library/jest-dom added; `"test": "vitest run"`; `test: { environment: 'jsdom', globals: true }` block.

Run the gate: `cd src/leopard-web && npm test` → **18/18 green**.

**2. `docs/property-inventory.md`** — full property palette with stable versioned IDs (`raid.pull.bossEndPctHp@v1` …). All 7 artifacts. Every field traced to its C# source file + line. Disk-to-confirm items flagged. This is the source table for provenance attributes on future XML emits.

**3. `docs/query-builder-design-prompt.md`** — locked constraints (versioned envelope, canonical serialization, graph-first storage, lens framing, provenance on every emitted property, computable confidence, UserIntent block, format bake-off) + UX framing + open questions for the composer build.

---

## Outstanding before merging the PR

### Windows-side (Derek does these, not the builder)

1. **`dotnet build src/leopard-host`** — the Vite bundle hash changed (`C16AIMjH` → `D7mIwyRv`). The debug/dev launch picks up the new file automatically. The release manifest needs a dotnet build to update.
2. **`dotnet test src/leopard-host.Tests`** — confirm C# tests still 9/9 (no regression from the AskPanel change, which is JS-only, but confirm anyway).
3. **Disk-to-confirm fields** — open a `.career.json`, `.trends.v2.json`, `.shape.v1.json` from `%LOCALAPPDATA%\Leopard\cache`. Confirm every field marked `disk-to-confirm` in `docs/property-inventory.md` exists with the stated type. Update the inventory to `repo-confirmed` + source file/line.

---

## Launching the app (figured out today — write this down)

The debug exe needs `ASPNETCORE_ENVIRONMENT=Development` to load the static web assets from the source `wwwroot`. Without it, the WebView2 gets a 404.

Two-step launch (run both, in either order — they're independent):

```powershell
# 1. Web dev server (Vite, port 5273) — only needed for live-reload dev work
#    NOT needed for a demo; the .NET host serves the built bundle from wwwroot
Start-Process powershell -ArgumentList '-NoExit', '-Command', 'cd D:\work\leopard\src\leopard-web; npm run dev'

# 2. The app itself
$env:ASPNETCORE_ENVIRONMENT = "Development"
Start-Process "D:\work\leopard\src\leopard-host\bin\Debug\net9.0-windows\leopard-host.exe"
```

For a **demo** (no live-reload needed): skip step 1, just run step 2.

---

## What's next — the composer (lens) build

This is the next major session. The ensure rung gated it; it's now unblocked.

### The locked decision (from `docs/query-builder-design-prompt.md`)

The composer **replaces** the three zoom buttons ("This night / A boss's career / Recent form") with **intent-named lenses** as the front door. Those three zooms don't disappear — they become the first three template lenses in the lens picker. Editing a lens is the on-ramp; no blank page.

### Domain for the first vertical slice

**A boss's career** — cleanest structured substrate (`.career.json` is already field-addressable JSON, no C# work needed), zero new engine math, answers "are we getting better at this boss?" from real history.

### Acceptance bar for the slice

The **demystification moment**: the user sees "What Leopard knows" assemble *before* asking. Picking or dropping a property visibly changes the list. The user trusts the answer because they watched it get built.

### The seam (already in place)

`CanonicalContext` is the plug point. Today's three zooms produce one from API text blobs or a structured `enc`. The composer produces one from assembled XML:

```js
new ContextBuilder().setXml(xml, scopeLabel).freeze()
// same interface, same two consumers (render + serialize), nothing else changes
```

`buildMessages` and `chatStream` are untouched.

### Before the composer session starts — read these

- `docs/query-builder-design-prompt.md` — the full locked constraints and UX framing (15 min read; don't skip the confidence propagation rule or the format bake-off note)
- `docs/property-inventory.md` — the palette; the composer references these stable IDs
- `src/leopard-web/src/context.js` — the seam the composer plugs into (short; read it)
- `src/leopard-web/src/AskPanel.jsx` — the integration point today; understand it before adding the lens picker
- `src/leopard-web/src/PipelineTab.jsx:104–159` — the `DrillIn` component; the composer extends this pattern, doesn't start a new surface

### Deferred (not the composer session either)

- **Structured box-score emit (C#)** — `BoxScore.cs` and `CareerSummary.cs` produce markdown blobs. Field-level selection from the night and career zooms requires a structured emit alongside the blob. Needs the Tempo sibling repo and the Windows toolchain. Documented in the property inventory with source lines.

---

## Repo state at end of session

```
branch: inventory-canonical-context (1 commit ahead of master)
PR:     https://github.com/djcdevelopment/leopard/pull/1 (draft)
tests:  18/18 green (cd src/leopard-web && npm test)
build:  clean (vite build succeeded)
C# tests: not re-run this session (web-only changes; run dotnet test to confirm)
```

Untracked files left on disk (not on the branch, not Derek's concern right now):
- `docs/rack-design-prompt.md`
- `retros/ask-zoom-grounding-2026-06-07.md`
