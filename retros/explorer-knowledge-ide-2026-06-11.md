# Retro: the Explorer — a dead cloud plan, a re-pasted mockup, and the knowledge-library IDE

*One session took the eighth surface from a failed cloud ultraplan to shipped: the Explorer,
where the ported RaidUI math becomes composable knowledge objects in a HomeSite-1997-density
IDE, compiled into a digest-signed slice-XML contract and run as an investigation against the
local model. Plus the launch bug the ship immediately exposed — and its fix.*
*Date: 2026-06-11 · Scope: `0ecba13` (one feature commit, 15 files, +1,597) + this retro commit*

---

## What shipped

**The Explorer** (`0ecba13`): a new full-width tab built from Derek's design mockup — three
panes in dense IDE chrome. Left: the **knowledge tree** (`knowledge.js`, a 16-entry registry
in the lens.js pure-data pattern) with starred seed questions, availability dots, and
`+`-to-contract; four objects are live (Pulse/six signals, Cohesion graph/affinity, Reaction
spread/player scores, Pull diff — the four RaidUI ports that already had endpoints), the rest
ghosted at 45% opacity so phase 2 only flips registry entries. Center: the **compiled.context
editor** (`contract.js` compiler → `<context digest slices><pull><slice object scope rep agg
time tok/>` XML; sha256 digest over the inner bytes per the lens.js rule; chars/4 token
estimates; display-only payload folding) with a status line ("4 slices · ~2.1k tokens ·
Balanced coverage"), an **Investigation** box wired through the *same*
`buildMessages`/`chatStream` path AskPanel uses, and an **output dock** (Response/Sources/Raw
JSON real; Reasoning Trace/Tool Calls/Diagnostics honestly stubbed). Right: the **Properties
inspector** — provenance metadata from the registry, slice dropdowns that recompile the
contract on every change (digest and tok attrs visibly move; signals `rep`/`agg`/`time` and
players `scope` genuinely reshape payloads, the rest are flagged annotation-only), lineage
lines, and RaidUI-style previews (sparkline stack, leaderboard bars, N×N affinity heatmap,
diff rows).

**The launch fix, same commit**: Derek's first real launch 404'd the entire UI. Production
static files resolved from the *launcher's cwd*; Development had masked this forever via the
StaticWebAssets manifest. `Program.cs` now pins `ContentRootPath` to `AppContext.BaseDirectory`
and the csproj copies `wwwroot\**` to bin on build — the exe is location-independent (ADR-0008).

Tests: vitest 18 → **37** (registry shape + compiler golden-XML/digest/ordering suites,
written and green *before* any component existed); xUnit **62/62** unchanged. Verified live:
endpoints probed against the real 2026-06-09 night, cross-encounter diff guard confirmed, and
the final check launched the exe Production-mode from `C:\` — the exact failing case — and got
200 with the fresh bundle hash. ADR-0007 (client-side slice compiler) and ADR-0008 extracted
in this retro commit; `docs/feature-order.md` and `README.md` updated to the eight-surface
reality.

---

## Engineering Lead perspective

The load-bearing decision is ADR-0007: the slice contract compiles **client-side, in new pure
modules, with lens.js and context.js untouched**. The precedent held beautifully — `contract.js`
imports `sha256hex`, copies the escape/sort/digest-over-inner rules, and feeds its output
through `ContextBuilder.setText().freeze()`, so the display==send invariant (ADR-0004) covers
the new surface for free. The career lens turns out to have been the right rehearsal: the
Explorer is the same shape at a higher power — palette→registry, properties→slices,
"What Leopard knows"→compiled.context.

Build order mattered: api wrappers → registry+tests → compiler+tests → previews → panes →
orchestrator → registration. The 19 new vitest tests existed before the first JSX file, which
is why the only mid-build defects were trivia (a stray CJK character typed into a comment, a
circular-import near-miss when `ExplorerProperties` initially imported a helper from
`ExplorerTab` — moved to `contract.js` where it belonged). The registry's REAL_KEYS map in
`ExplorerProperties.jsx` is the honest-annotation mechanism: dropdowns the compiler doesn't
branch on are labeled as recorded-not-effective rather than silently cosmetic.

The content-root fix is small but architecturally meaningful (ADR-0008): the product's
"double-click .exe" promise was silently false in Production, and the fix makes the
npm→dotnet build pairing *structural* — dotnet build is now what refreshes the served bundle.

Debt ledger: the `buildCareerLens` vitest test is now **five retros unwritten** (the contract
compiler got its golden tests day one; the older sibling still has none). New debt, named in
ADR-0007: registry metadata is hand-maintained JS that can drift from C# artifact reality, and
the digest signs client-compiled bytes with no server attestation. Both fine at this scale,
both have named upgrade paths.

---

## Project / Program Manager perspective

Scope tracked the plan unusually tightly — because the plan was re-made *after* the design
arrived. The session opened with a sunk cost: the cloud ultraplan crashed mid-flight
(error_during_execution after Derek had answered its clarifying questions in the desktop app),
taking the design image with it. The local replan recovered in one pass: three Explore agents
+ one Plan agent, two AskUserQuestion decisions (Workbench-style surface; thin slice first),
and the re-pasted mockup which **changed the deliverable category** — from "viz panels for the
ports" to "query-builder IDE where the ports are context objects." That pivot cost nothing
because no code existed yet. The lesson cuts both ways: the cloud failure wasted an evening
slot, but planning locally against the actual repos produced corrections (diff endpoint takes
three params; meters already embedded in affinity; `/ollama` mapped in prod) that the blind
cloud session could never have caught — consistent with the standing ultraplan-cloud
constraint note.

Phase boundaries held. Phase 1 shipped with zero new host endpoints; coverage/segments/
classify are plumbing-only phase 2 items with the C# already tested; meters and Shape are
flagged as near-free flips. One schedule surprise: the "done" declaration was premature by one
launch — verification passed in Development mode, Derek's Production launch failed in under
ten minutes. The fix itself took ~15 minutes; the gap was in verification mode parity, not in
the code.

Risk surface: retired — the eight ported modules are no longer invisible math; the four
highest-value ones have a surface and the rest have a visible, ghosted home. New — the
investigation path reuses the `/ollama` provider plumbing, which still carries the known
proxy-target mismatch (11434 vs the llama-server on 8080); the Explorer inherits whatever Ask
inherits there.

---

## QA / Verification perspective

What's covered: 19 new vitest tests on the pure core — registry shape invariants (ids unique,
live⇒api, defaults⊆options), compiler golden XML against a synthetic night, digest stability
across runs, input-order independence (canonical sort), attr-change⇒digest-change, the
around-deaths time mask, leaders-scope truncation honesty, explicit-absence serialization for
missing artifacts, escape discipline, the chars/4 estimator, coverage labels. The existing 18
context tests and 62 xUnit ran untouched — the host change was config-only and the suite
confirmed it.

What was verified by independent evidence: all four endpoints probed against the real
2026-06-09 M+ night (signals 53 KB / players 7 KB / affinity 17 KB, 200s; cross-encounter
diff correctly rejected with `cross_encounter`; self-diff 200). The launch fix was verified
**against the exact failure case** — Production mode, cwd `C:\`, fresh process — not just
re-run in the mode that had masked the bug.

The miss worth naming as a transferable pattern: the original smoke test launched with
`ASPNETCORE_ENVIRONMENT=Development`, which routes static files through the StaticWebAssets
manifest — so "/" returned 200 for a reason that doesn't exist in the user's launch path.
**Verify in the mode the user will actually use.** The 404 screenshot arrived within minutes
of the "done" message.

Not covered, accepted: no end-to-end investigation run against a live model this session (the
ask path is byte-identical reuse of AskPanel's, but a streamed answer through the Explorer has
not been observed); no component-level React tests (consistent with the repo's pattern — pure
logic gets tests, JSX gets manual passes); the M+ night can't exercise the diff compare
dropdown (one pull per boss) — needs a raid night.

---

## Operator perspective

The ultraplan thing was genuinely weird from my side — the plan showed up in my Windows Claude
app, asked me good questions, and then archived itself like it was never there. Knowing now
that the answers died with the container, I'd still rather have the plan made locally where it
can read the actual repos. The constraint note exists for a reason; this is the third way a
cloud session has eaten work.

The mockup mattered more than I expected it to. I drew it as "what if Leopard looked like
HomeSite" — and what came back wasn't panels bolted onto tabs, it was the query-builder I've
been circling since the property inventory: the tree IS the drill, the contract IS the lens,
the XML is right there with a digest on it. Watching the token counts move when I flip a slice
dropdown is the demystification thesis working on me, and I built the thing the thesis is for.

Calls I made: Workbench over more tabs (seven tabs was already the ceiling), thin slice over
all-eight (the ensure/verify/slice discipline has paid off every time), and punting the
single-B70 model question when I caught myself about to derail the session — discoverlay and
vllama already solve it, it can wait. The 404 on first launch was annoying for exactly four
minutes, and then it turned into the kind of fix I actually want: the exe working from
anywhere is what "double-click" is supposed to mean.

---

## How we worked together (human ↔ AI)

### What worked well

- **The cloud failure triaged cleanly because the constraint was already in memory.** The AI's
  first response to the dead session explained the desktop-app handoff + archive behavior,
  amended the existing ultraplan-constraints note with the third failure mode, and offered the
  local path — no time lost re-deriving why cloud sessions are untrustworthy for this repo.
- **AskUserQuestion with ASCII layout previews, before the image arrived.** The
  Workbench-vs-more-tabs decision was made on rendered mockups while the real screenshot was
  still lost. When the image landed, it *upgraded* the chosen direction rather than
  contradicting it — the sequencing (lock layout class and scope first, fold in design detail
  second) kept the lost image from blocking anything.
- **The re-pasted mockup was treated as a spec, not an inspiration.** The AI read the nav
  text, the slice attributes, the Properties rows, the status line out of the screenshot and
  mapped each to existing code (`lens.js` digest rules, `wintoggle` patterns, the artifact
  endpoints). The shipped surface is recognizably the mockup.
- **Grounded agent corrections prevented real bugs.** The Plan agent, told to verify reuse
  claims against actual files, came back with: diff takes `(name, a, b)` not `(a, b)`; meters
  already ship inside the affinity payload; `/ollama` is mapped in prod. All three would have
  been integration failures.
- **Tests-first on the pure core.** 19 vitest tests green before any JSX existed meant the
  component phase was assembly, not debugging.
- **The 404 screenshot was a complete bug report.** One image, no prose — and the
  Dev-vs-Production static-file asymmetry was isolated in two tool calls because the smoke
  test's own launch command was still in context to diff against.

### What didn't

- **Verification mode parity failure (AI-side).** The smoke test set
  `ASPNETCORE_ENVIRONMENT=Development` — for the WebView2-vs-Vite reason — and thereby
  validated `/` through a static-assets path that doesn't exist in the user's launch. "Done
  and verified" was declared on evidence that didn't cover the handed-over command. The user
  found the gap in minutes.
- **The handed-over launch command was untested as-written.** The retro-worthy detail: the
  exact `Start-Process` line in the wrap-up message had never been executed. Commands offered
  to the operator deserve the same verification as code.
- **A build-lock stumble.** The first rebuild after the fix failed on MSB3027 because the
  user's 404'd window still held the exe. Recoverable in one step, but the AI launched the
  build without checking for a running instance it knew existed (it had just seen the
  screenshot of it).
- **Cloud ultraplan burned the first attempt entirely** (operator-side tooling, not a
  collaboration failure per se): plan questions answered, then error_during_execution, answers
  unrecoverable. Third distinct cloud failure mode for this project.

### Patterns to repeat

- Plan locally for this repo; treat `/ultraplan` cloud as scaffold-only (now reinforced in
  the memory note with the mid-plan-crash failure mode).
- Lock the decision skeleton with AskUserQuestion + previews while waiting on missing inputs;
  fold the inputs in as upgrades.
- Pure-data registry + pure compiler + golden tests before any UI — the lens.js lineage now
  has two successful generations.
- Instruct Plan agents to verify every reuse claim against real files and to *flag stated
  facts that are wrong* — the corrections list is where the value was.

### Patterns to change

- **Final verification must execute the exact artifact handed to the operator** — same
  command, same environment, same cwd class. A smoke test that passes for environment-specific
  reasons is worse than none; it converts a bug into a confident claim.
- Before any `dotnet build` of the host, check for a running `leopard-host.exe` (now also
  named in ADR-0008's consequences).

---

## Lessons learned

1. **Development mode can structurally mask Production bugs.** ASP.NET's StaticWebAssets
   manifest makes static files work in Development *no matter where the content root points*.
   Any verification of serving behavior must run Production-mode from a foreign cwd.
2. **A good mockup is a requirements document.** Reading the screenshot's literal text (slice
   attrs, properties rows, status line) produced a tighter spec than the prose request did —
   "include the parser ports in the UI" became "the ports are knowledge objects in a contract
   compiler" only because the image said so.
3. **Canonicalization rules are infrastructure.** lens.js's escape/sort/digest-over-inner
   decisions, made for nine roster properties, absorbed a 16-object slice compiler without
   modification. Small invariants chosen carefully compound.
4. **Ghost entries are cheap roadmap.** Shipping the full taxonomy with `status:'ghost'` makes
   phase 2 a flip-and-fetch change, gives users a visible map of what's coming, and forced the
   "what maps to what" thinking (Wipe cause → WipeClassifier) at design time instead of later.
5. **Honest annotation beats silent cosmetics.** Slice dropdowns the compiler doesn't branch
   on yet are labeled as annotation-only in the UI. The alternative — controls that look
   effective and aren't — is exactly the control-legibility failure the refinement phase
   forbids.

---

## Next moves

- **Phase 2 of the Explorer**: `/api/coverage`, `/api/segments`, `/api/classify` + parse-time
  caches (C# modules already tested — plumbing only), then registry flips. Near-free flips
  first: Movement meters (already in the affinity payload) and Shape (`/api/shape/density`
  already serves). Plan: `C:\Users\derek\.claude\plans\jaunty-coalescing-pine.md`.
- **Run one real investigation end-to-end** on a raid night (multi-pull boss) with the local
  model up — exercises the diff compare picker and the streamed Response path, the two things
  this session never observed live.
- **The `/ollama` proxy-target mismatch** (11434 vs llama-server :8080) now affects two
  surfaces (Ask + Explorer). Resolve via the discoverlay/vllama single-B70 patterns Derek
  flagged — parked deliberately this session, but it's accruing interest.
- **`buildCareerLens` vitest test** — fifth retro. The contract compiler's golden-test suite
  is sitting right there as a template; this is now a 20-minute task with no excuse.
- Consider folding the knowledge-object ids (`signals.pull@v1` …) into
  `docs/property-inventory.md` so the versioned-id namespace stays single-sourced.

---

## Acceptance gates met

- [x] Four exposed RaidUI-port modules surfaced end-to-end (tree → contract → XML → ask path)
- [x] Full mockup taxonomy present in the tree (live + ghosted), phase 2 = registry flips
- [x] Contract XML matches the mockup schema with live digest + per-slice tok attrs
- [x] display==send structural via ContextBuilder; payload folding is display-only
- [x] Existing surfaces untouched: 780px layout preserved, 18 prior vitest + 62 xUnit green
- [x] npm build → dotnet build pairing honored; endpoints verified against a real night
- [x] Exe launches from any cwd in Production (the 404 regression case re-tested explicitly)
- [ ] Streamed investigation observed against a live model — deferred: provider wasn't
      running this session; path is byte-identical AskPanel reuse
- [ ] Diff compare exercised on real data — deferred: the verified night is M+ (one pull
      per boss); needs a raid night
