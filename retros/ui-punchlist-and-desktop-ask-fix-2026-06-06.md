# Retro: UI punch-list 0-4 + the desktop Ask was never wired

*A pre-raid status check turned into a five-item UI refactor and the discovery that
"Ask" had never actually worked in the packaged desktop window — only via the dev server.*
*Date: 2026-06-06 - Scope: uncommitted working tree (11 files), no commits yet this session*

---

## What shipped

The session opened 45 minutes before raid with a simple ask: what state is Leopard in?
That answered (green, 5/5 tests, all surfaces live), it became "show me the app," which exposed
real UI/UX friction once Derek actually looked at it: a duplicated night-selector on every tab,
a parse flow that dead-ended before Ask, and a Trends pull-window control that did nothing. A
plan was approved (items 0-4) and all five landed, plus two latent bugs that only surfaced
because Derek looked at the running window with his own eyes.

The work is entirely in the working tree, uncommitted by design (pending Derek's review):
**11 files** - `src/leopard-web/{App,LeopardTab,TrendsTab,PipelineTab,SetupTab}.jsx`,
`src/leopard-web/src/{provider.js,styles.css}`, `src/leopard-web/vite.config.js`,
`src/leopard-host/{Program.cs,TrendsArtifact.cs}`, and `docs/feature-order.md`. Tests stayed
**5/5** throughout. The headline non-obvious find: the host's `/ollama` proxy targeted
`localhost`, which .NET resolves to IPv6 (`::1`) first on Windows, where Ollama doesn't listen -
so the desktop app's "Ask" had never reached the model. It works only via Vite's dev proxy. That
bug shipped in the merge `0c81c6b` and went unnoticed because every prior Ask test used either
the dev server or a direct `curl` to `:11434`.

---

## Engineering Lead perspective

Five changes, each small but one structural. **Item 1** (lift night-selection to `App.jsx`) is
the real refactor: three tabs each independently called `getLogs()`, held their own `selected`
state, and rendered their own `<select>`. That's now one source of truth in `App` passed down as
`night`/`hasParsed` props, with each tab driving its data fetch off a `useEffect([night])`. The
duplication is gone and selection persists across tabs. **Items 2/3** are small and clean - a
Setup-to-Ask handoff via an `onGoToNight` callback, and a model-default that prefers whatever is
resident (`loadedModels()` hitting `/ollama/api/ps`) before falling back to a preference list now
headed by `b70`/`mistral`. **Item 4** (selectable Trends window) touched both tiers: the
projection already accepted any window size, so `TrendsArtifact` now precomputes 4/6/8/10 into a
`windows`/`coherences` map keyed by size, and the client toggles with no recompute.

Two decisions worth flagging as architectural. First, **artifact cache versioning**: the parse
step only regenerates an artifact when its cache file is *missing*, and caches are keyed by file
mtime - so re-parsing an unchanged log to pick up a new schema was a silent no-op. Bumping the
trends cache filename to `.trends.v2.json` makes old artifacts read as absent, so a re-parse
regenerates them. This is a reusable pattern: any artifact schema change needs a version
dimension on the cache key, not just mtime. Second, the **ship pipeline**: `vite.config.js` now
builds straight into `src/leopard-host/wwwroot` (`emptyOutDir: true`, with
`public/voidspire-evidence.md` re-copied each build), and the host serves `index.html` with
`no-store`. Together these kill the entire class of "stale bundle" drift that opened the session.

The IPv4 proxy fix (`Program.cs`, `localhost` -> `127.0.0.1`) is one line but the highest-value
change of the session - it's the difference between Ask working and not working in the shipped
product. No new tech debt of note; the two acknowledged gaps are test coverage for the proxy and
the v2 trends shape (neither is covered yet), and the legacy v1-shape fallback in `TrendsTab` is
now dead code (un-reparsed nights 404 instead) - harmless, removable later.

---

## Project / Program Manager perspective

Scope was crisp: items 0-4, explicitly enumerated and approved via plan mode before any code.
All five completed in-session. The notable schedule reality is that the *planned* work (the
refactor) was the easy part - it went in cleanly and built first try. The *unplanned* work (two
bugs) consumed more wall-clock and was where the value concentrated. That's the tell: the plan
covered what we knew about; the screenshots covered what we didn't.

Deferred, with reasons: (1) the git commit - left uncommitted so Derek reviews the 11-file diff
as a unit before it lands; (2) tests for the proxy path and the trends v2 artifact shape - real
gaps, but not raid-blocking; (3) the visible payoff of the window toggle - the entire parsed
corpus maxes at 2 pulls per boss, so the 4/6/8/10 control correctly shows no change until a
progression night exists (tonight's raid produces one). Risk surface: one new risk retired
permanently (stale-bundle drift, via the build pipeline change) and one major latent risk retired
(desktop Ask was non-functional). New dependency on behavior: the host now assumes Ollama on
`127.0.0.1:11434` specifically, not `localhost` - documented in the fix comment and memory.

The decision delegated to the AI - whether item 4 should make the toggle *work* or just remove
the dead control - was made in Derek's favor (make it work), surfaced in the plan, and left
vetoable at approval. He didn't veto. That's the right shape for "I'm busy, don't ask unless
stuck": decide, document the decision prominently, leave the exit ramp.

---

## QA / Verification perspective

This session was a clinic in verification-layer discipline, mostly by negative example. The build
and data layers were verified rigorously: the served bundle was confirmed by counting "Raid
night" occurrences in the live JS (3 in old, 1 in new) and checking the served asset hash; the
v2 trends artifact was confirmed by re-parsing and inspecting the JSON shape (windows keyed by
4/6/8/10, defaultWindow 6); tests held at 5/5 across every rebuild; the Ask path was validated
end-to-end through the host proxy (`POST /ollama/api/chat` returned a clean `'ready'`,
`done_reason: stop`).

But two bugs slipped past all of that because they live at the **pixel/runtime layer**, which the
AI cannot observe. "Verified the build serves new code" is not "verified the user sees new code" -
WebView2's HTTP cache replayed the old bundle, and only Derek's "still not seeing" caught it.
Likewise, every server-side check was green while the Leopard tab sat on "Looking for a local
model" - the proxy bug was invisible to `curl` (which picks IPv4 fast) and only showed in the
screenshot. The transferable pattern: **grep the actually-served artifact, not the source** was
high-value; **distinguish verification layers explicitly** was the missing discipline.

Regression surface: the refactor changed prop contracts on four tabs and the parse return shape
(`onParsed(names)`); no automated test guards these, so the screenshots were the regression suite.
Not covered and acceptable for now: visual layout, the proxy, the v2 artifact schema. Not covered
and worth fixing: a smoke test that drives `/ollama/api/tags` *through the host* would have caught
the IPv4 bug instantly - that's the test to write first post-raid.

---

## Operator perspective

*(first person - Derek)*

I came in to check status before raid and ended up looking at the actual app, which is the only
reason any of this got found. The thing I keep relearning: green tests and "data path verified"
don't mean the product works. I clicked Trends and the window selector did nothing; I clicked
Leopard and it couldn't find a model. Neither showed up in any check until I looked.

The judgment call I delegated was the Trends window - make it real or kill it. I'd reported it as
a bug, so making it work was the right read, and I'm glad it asked itself that question instead of
defaulting to the lazy delete. The call I had to keep making was "still not seeing" - twice. That
felt like friction at the time but it was the system working: I'm the only verification layer that
can see pixels, so when the AI says "verified" and I see something else, my job is to push back
until we reconcile. Both times the reconciliation found a real bug (cache, then proxy).

What felt right: the refusal to guess. When I asked "is that me or you?", instead of just
relaunching it counted "Raid night" in the live bundle and proved the server was serving new code
- which correctly pointed at my window, not the build. What felt off: I had to relaunch the window
more times than I'd like, and some of that was the AI not pinning down early that the window kept
closing on me. But the proxy find alone justified the whole detour - that bug would have bitten me
mid-raid-review when I actually tried to Ask something.

---

## How we worked together (human <-> AI)

### What worked well

- **The screenshots were the unlock.** Three images ended a loop of "the server is serving new
  code" vs "I'm not seeing it." They simultaneously confirmed items 0/1/4 worked AND exposed the
  "Looking for a local model" bug. Nothing server-side would have surfaced that. When the human
  can see the artifact, a screenshot beats a paragraph of diagnostics.
- **"Is that me or you?" forced grounded diagnosis.** Instead of relaunching on reflex, the AI
  counted a distinguishing string ("Raid night") in the *live served bundle* - 1 occurrence = new
  code - and correctly concluded the build was fine and the window was stale. Proving which side
  owns a problem before acting saved a wrong fix.
- **Verification caught its own gap.** Re-parsing to test the v2 trends artifact returned the OLD
  shape, which exposed that mtime-keyed caches no-op on re-parse. The test of the feature found a
  bug in the feature's deployment story. Verifying end-to-end, not just unit-locally, paid off.
- **Plan-mode decision handling.** Item 4's make-it-vs-kill-it fork was decided autonomously,
  stated prominently in the plan, and left vetoable - matching the operator's "don't ask unless
  stuck" while not hiding a real product choice.
- **Reading ground truth before claiming.** Projection availability was checked by dumping type
  names out of `Tempo.Projections.dll`; the proxy bug was confirmed by timing `localhost` (2.2s
  fail) vs `127.0.0.1` (0.004s) before editing. Few claims were made from memory.

### What didn't

- **"Verified" was overloaded.** The AI repeatedly said items were "verified" when it had only
  confirmed the build/data layer, not the rendered window. The operator had to say "still not
  seeing" twice to force the distinction. The layer should have been named every time:
  "verified at the served-bundle layer; needs your eyes for the pixels."
- **Stale-build root cause came late-ish.** When Derek first reported UI problems, the very first
  diagnostic should have been "is the served bundle current?" - it turned out to reframe all three
  complaints. It was found, but after some code-reading that assumed the source matched what he saw.
- **Window-lifecycle thrash.** The desktop window closed repeatedly (clean exit 0). The AI cycled
  through theories (background-task teardown, Start-Process detach) before landing on the simplest
  explanation - the operator closing windows that looked wrong - which was never fully pinned.
- **PowerShell 5.1 papercuts.** An `if`-as-expression and a couple of `curl | python` pipes failed
  under the shell's quirks, burning tool calls. The environment's constraints are known and should
  have been respected on the first try.

### Patterns to repeat

- Grep/count a distinguishing string in the **live served artifact** to prove old-vs-new, rather
  than trusting that a rebuild reached the client.
- Version-suffix a cache key (`.trends.v2.json`) when an artifact schema changes; never rely on
  mtime alone to invalidate.
- Ask the operator for a screenshot the moment a "server says X, you see Y" disagreement appears.

### Patterns to change

- **State the verification layer in every "done" claim.** Build-green / data-correct /
  pixel-confirmed are three different things; say which one.
- **On any UI-mismatch report, check served-bundle freshness and process-alive FIRST**, before
  reading source as if it's what's running.

---

## Lessons learned

1. **"Verified" is meaningless without a layer.** Build compiles, data is correct, and the user
   sees it are three independent claims. Two of this session's bugs lived in the gap between data
   and pixels - exactly where the AI is blind and the human is the only sensor.
2. **A static SPA behind WebView2 has two cache traps.** The .NET staticwebassets manifest (needs
   a host rebuild to pick up new hashed filenames) and WebView2's own HTTP cache (needs `no-store`
   on `index.html`, since the HTML points at immutable hashed assets). Hash the assets, never-cache
   the HTML.
3. **On Windows, prefer `127.0.0.1` over `localhost` for loopback HttpClient calls.** .NET tries
   IPv6 `::1` first; services bound IPv4-only stall ~2s and fail. `curl` hides this with Happy
   Eyeballs, so it won't reproduce from the shell.
4. **mtime-keyed caches silently no-op on schema changes.** If regeneration is gated on "cache
   file missing," a new artifact shape needs a new cache key (version suffix), or re-parsing an
   unchanged input does nothing.
5. **The human looking at the real thing is irreplaceable verification.** Two latent bugs - one a
   shipped non-functional feature - were found only because the operator opened the window. Budget
   for it; it is not redundant with automated checks.

---

## Next moves

- **Commit the 11-file diff as a unit** once Derek has reviewed - suggest staging by file, not
  `git add .`. A single commit: "fix: shared night-picker, Setup->Ask handoff, selectable Trends
  window, desktop Ask (IPv4 proxy) + bundle/cache hardening."
- **Write the two missing tests** post-raid: (a) a host smoke that GETs `/ollama/api/tags` through
  the proxy (would have caught the IPv4 bug), (b) an assertion on the v2 trends artifact shape.
- **Re-warm the B70 model after raid if idle** - 30-min keep-alive expires; the default now
  correctly prefers the resident/`b70` model so a cold first-Ask is the only cost. See
  `C:\Users\derek\.claude\projects\D--work-leopard\memory\ollama-proxy-ipv4.md`.
- **Validate the Trends toggle on a real progression night** - it is invisible on the current
  corpus (max 2 pulls/boss) and tonight's raid is the first dataset that exercises it.
- **Shape remains the next surface** with no plan doc - a short design brief mirroring
  `docs/pipeline-explorer-design-brief.md` is the highest-leverage planning move (see
  `docs/feature-order.md`).

---

## Acceptance gates met

- [x] Item 0 - stale-build drift fixed (vite -> `wwwroot`, `no-store` on HTML); served bundle confirmed current
- [x] Item 1 - single shared night picker in `App.jsx`; persists across tabs (screenshot-confirmed)
- [x] Item 2 - Setup -> Ask handoff ("Ask about this night ->" CTA + carried selection)
- [x] Item 3 - Leopard defaults to resident/`b70` model via `loadedModels()`
- [x] Item 4 - selectable Trends window 4/6/8/10 (default 6); v2 artifact verified
- [x] Tests still 5/5
- [x] Desktop Ask validated end-to-end through the host proxy (IPv4 fix)
- [ ] Committed - deferred, uncommitted by design pending operator review
- [ ] Tests for proxy + v2 trends shape - deferred, not raid-blocking
