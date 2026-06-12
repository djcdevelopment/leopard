# Retro: B70 first light, the night lens, and the overtime axis

*The evening session after phase 2: the provider became config and the dual-B70 ran Ask and
the Explorer for the first time; the night lens made the opening prompt a composer; and the
operator's live-fire diagnosis — "even with better data they are pretty vague" — became two
over-time slices before the session ended. Five commits, three field-found bugs fixed
forward, and the phase-2 retro's deferred acceptance gates exercised on a real raid night.*
*Date: 2026-06-11 (late evening) · Scope: `90e5bf8..fec78da` (5 commits) + this retro commit*

---

## What shipped

**The provider contract, realized** (`90e5bf8`): `askProviderUrl` + `askProviderApi`
(`"ollama"` | `"openai"`) in config.json; the host's `/llm/*` streaming proxy to the
configured URL; dual-protocol `provider.js` with pure, tested parsers; Vite dev routed
through the host so config applies in every mode; `PUT /api/config` merge-on-null (a Setup
save no longer wipes hand-edited fields). Verified against a new dual-protocol mock
(`tools/mock-provider.mjs`) before any GPU was involved. The pre-build protocol check
mattered: vllama and llama-server are **OpenAI-only**, so the three-session-old plan of
"point the proxy at :8090" would have shipped a protocol mismatch. ADR-0010.

**The night lens** (`58823f9`): `BoxScore.BuildJson` — the box score's exact figures as
structured JSON (`.night.v1.json`, `GET /api/night`) alongside the byte-identical markdown —
plus `NIGHT_PALETTE`/`buildNightLens` and an AskPanel night zoom that composes property-by-
property with markdown fallback for old vintages. The oldest documented gate in the
query-builder line ("structured emit deferred to a Windows session"), closed.

**The over-time slices** (`fec78da`): `progression.encounter@v1` live off `/api/night` (the
boss-night rollup anchored on "THE SELECTED PULL in that sequence: pull 11 of 14 …") and the
new `trend.window@v1` off `/api/trends` (rule-row deltas + coherence). Zero new endpoints.
Eleven live knowledge objects.

**Two field fixes** from the first live B70 run: provider detection retries every 5 s while
absent (`db0ba32` — the model server finished loading after the app window opened and the
one-shot detect dead-ended on the banner), and chat errors surface the provider's message
(`b0234f8` — a bare "HTTP 400" hid llama-server's `exceed_context_size_error`).

Tests: vitest 57 → **73**, xUnit 67 → **68**. Verified live at every step — the mock, the
real night artifact, and finally the operator's own investigations streaming off
`tempo-b70-second` on the second B70 with an 11-slice vocabulary.

---

## Engineering Lead perspective

The decision that made the evening possible was made *before* the session: checking vllama's
actual surface (`FacadeHost.cs`: `/v1/models`, `/v1/chat/completions`, nothing Ollama-shaped)
killed the "configurable proxy target" framing the retro corpus had been carrying and
replaced it with the provider contract's own answer — protocol is part of the entry. The
implementation honors the repo's layering: the host proxy stays dumb (bytes through), the
protocol intelligence lives in `provider.js` as pure exported parsers, and the seam between
them is one config read. The merge-on-null PUT is the kind of small fix that only gets made
when you're about to *create* more reasons to hit the footgun — documenting it was the
original plan until writing the caveat made the fix look cheaper than the warning.

The night lens deliberately duplicated rather than refactored: `Build` (markdown) is
untouched and `BuildJson` mirrors its small expressions, because the markdown blob is
load-bearing in three places tonight and a shared-model refactor under it had no test
oracle. The cost is two copies of four LINQ expressions; the alternative cost was risking
byte-drift in the shipping path the same evening it gained a sibling. `lens.js` now has two
palettes and the encounter-nesting pattern (`<encounter>` blocks inside `<night>`) that the
trend lens will reuse.

Debt: REAL_KEYS is now an 11-entry hand-synced mirror (flagged in ADR-0007, growing);
`fmtDur` exists in both `lens.js` and `contract.js` (deliberate, same copy-rules precedent,
but a third copy should trigger extraction); the B70 launch script's 4096-token default
context is a foot-gun documented in memory and ADR-0010's consequences but not yet changed in
`b70tools` (different repo).

---

## Project / Program Manager perspective

This session was the live-fire counterpart to the morning's plan-execution session, and the
schedule story is the loop closing in miniature: operator tests → reports symptom → diagnosis
with evidence → fix → re-test, three times, with median cycle time around fifteen minutes
("doesn't detect the model" → restart + retry-fix; "editor doesn't work" → context-size
reproduction + 32k restart; "works great"). The over-time slices went from operator
observation ("missing the overtime factor") to deployed in under an hour because both
backing endpoints already existed — the artifact-first discipline keeps paying compound
interest.

Unplanned scope absorbed cleanly: the orphaned-GPU investigation (an Ollama runner named
`llama-server.exe` survived its parent's kill and held 15.5 GB), the detection race, the
context ceiling. Risk retired: the `/ollama` proxy mismatch — three sessions old, named in
two memories and two retros — is gone as a class, not patched as an instance. Risk surface
now: the B70 script's context default (operational, lives in b70tools), and the single-entry
provider config (no list/picker — accepted in ADR-0010 with a named upgrade path).

The phase-2 retro's two deferred acceptance gates were exercised by the operator on the
2026-06-06 raid night: a real wipe verdict slice (`classify.wipe@v1` on Pull 11, Midnight
Falls) and the diff compare inside a streamed B70 investigation. The plan→build→verify chain
that started with this morning's `/sup` is fully closed.

---

## QA / Verification perspective

New coverage: 5 provider-protocol tests (both wire formats, `[DONE]`/keep-alive/bare-JSON
discipline), 8 night-lens tests (encounter nesting, killed-vs-in-progress separation,
trend-confidence scaling, the explicit-absence rules, digest-over-inner), 3 over-time tests
(the selected-pull anchor line, the coherence rep toggle, absence + wrong-boss), 1 xUnit
night-artifact shape test, plus the eleven-slice digest-stability compile.

Verified by independent evidence, in layers: the mock provider proved config plumbing with
zero GPU; `/api/night` proved the structured emit on the re-parsed real night; the operator's
screenshot proved the eleven-slice contract compiling against the 06-06 raid night; and the
final "works great" was a human watching tokens stream off the B70. The context-size
diagnosis is the pattern to keep: **reproduce with a controlled oversized input and read the
error body** before changing any state — the 6030-token synthetic prompt got llama-server to
name its own limit (`n_ctx 4096`), which turned "the editor does not seem to work" into a
one-parameter fix.

The miss to own: the AI told the operator "the app and Ollama are still running for you"
without re-checking; the process was gone, and the operator had to report it. The claim was
generated from session memory, not from a probe — the same class of error as the morning's
verification-mode-parity lesson, in a smaller frame: **liveness claims require a probe at
claim time.** Not covered, accepted: the night-lens and over-time UI paths are
manual-verified only (repo convention); `loadedModels` warm-detection on OpenAI providers
returns empty (documented in ADR-0010); the night-arc encounter match is name+difficulty
(the night artifact carries no pullId — fine within one night, noted for a v2 schema).

---

## Operator perspective

Tonight I watched my own GPU answer questions about my own raid through a pipeline I can
read end to end. The B70 thing was supposed to be plumbing, and it mostly was — config file,
two fields — but the first investigation coming back vague taught me more than the
plumbing did. I looked at the answer and knew immediately what was missing: not better data,
the *time* axis. Pull 11 means nothing without pulls 1 through 10. I said so in one
sentence and the contract grew the two slices that fix it before I'd finished poking at the
coverage question.

The session had three "it doesn't work" moments and none of them dented the evening: the
model didn't detect (race — the app looked before the model finished loading), the Explorer
400'd (my nine slices blew a 4k context the AI had flagged as a risk at launch), and earlier
the GPU was holding 15 GB after "shutdown" (Ollama's runner playing dead-parent). Each one
got diagnosed with receipts — the command line of the orphan, the server's own error JSON —
which is the difference between debugging and guessing.

My one design steer mattered: the opening Leopard prompt is where the single-night story
should live, not buried in window math or pull-vs-pull. That's now true — the night lens IS
the opening prompt — and the Explorer keeps the other two time-perspectives as opt-in
slices. The demystification thesis again: I toggled "Deaths per pull" and watched the XML
change before asking.

---

## How we worked together (human ↔ AI)

### What worked well

- **The pre-build fact-check killed a wrong plan cheaply.** The retro corpus said "point the
  proxy at :8090"; one grep of vllama's FacadeHost showed OpenAI-only routes, and the
  provider-contract doc supplied the correct architecture. Verifying the reuse claim before
  building is now 3-for-3 across sessions at preventing integration failures.
- **Live-fire triage with evidence, three times.** "Doesn't detect the model" → process
  probe + race diagnosis + restart + a retry fix. "Editor doesn't work" → a controlled
  6030-token reproduction that made llama-server name its own ceiling → one script parameter.
  Screenshot in, diagnosis + fix out, median ~15 minutes.
- **The operator's one-sentence product diagnosis became shipped code in under an hour.**
  "Missing the overtime factor" was a sharper spec than any planning doc — and it landed on
  data that was already served, so the fix was vocabulary, not infrastructure.
- **Each bug became a forward fix, not just a workaround.** The detection race got a retry
  loop (committed); the opaque 400 got error-body surfacing (committed); the context ceiling
  got documented in ADR-0010's consequences. Tonight's symptoms can't recur as mysteries.
- **The handover discipline held under repetition.** Every launch handed to the operator was
  probed first — health, bundle hash, and (after the first miss) the exact provider route the
  feature uses.

### What didn't

- **The AI claimed the app was "still running for you" without probing — it wasn't.** The
  operator had to report reality. Liveness claims from session memory instead of a
  fresh probe; same error class as the morning's verification-parity miss, smaller blast
  radius, same lesson.
- **The shutdown missed Ollama's runner child.** Name-filtered process kills
  (`ollama`, `ollama app`) orphaned `llama-server.exe` holding 15.5 GB of VRAM; the operator
  caught it in Task Manager. Shutdown of a provider should sweep its runner processes too.
- **The AI launched the app in parallel with a still-loading model server**, manufacturing
  the detection race the operator then hit. Sequencing was known (the script waits on
  health); the parallel launch traded correctness for speed.
- **A PowerShell 5.1 quoting bug ate a commit attempt** — embedded double quotes in a
  here-string broke native-arg passing; the night-lens commit had to be redone via
  `git commit -F`. Known platform quirk, now a known workaround.
- **The mock cleanup used a blanket `Stop-Process -Name node`** — nothing else was running
  on node, but PID-targeted is the discipline; blanket name-kills are how the next orphan
  hunt starts.

### Patterns to repeat

- Grep the actual surface of any service a plan claims compatibility with, before building
  against the claim.
- Reproduce failures with a controlled input sized to trigger them, and read the error body —
  make the failing system name its own limit.
- When the operator reports vagueness in model output, look for the missing *axis* in the
  context, not for more data on the existing axes.
- Fix the error message in the same commit-stretch as the error — tonight's opaque 400 would
  have cost the next session twenty minutes.

### Patterns to change

- **Never claim a process is running without probing it in the same turn.** `Get-Process` +
  endpoint probe, then the sentence.
- **Provider shutdown = parent AND runners** (Ollama's `llama-server.exe` under
  `%LOCALAPPDATA%\Programs\Ollama`); check GPU-holding processes by name pattern, kill by PID.
- **Sequence app launch after provider health** when starting both — or rely on the new
  detection retry, but don't manufacture the race.
- Multiline commit messages with quotes: `git commit -F <file>`, always, on PS 5.1.

---

## Lessons learned

1. **Contracts beat targets.** Making the provider a `{url, api}` config entry (the contract
   doc's shape) instead of a configurable proxy target (the accumulated assumption) is why
   the B70 worked the same evening — the protocol difference was the entire problem, and
   only one of the two framings could express it.
2. **A model answers along the axes its context provides.** Eleven slices about one pull
   produce description; one slice that says "pull 11 of 14, deaths trending down, best was
   23%" produces explanation. Context design is axis design.
3. **First live runs are diagnostic gold — schedule for the triage, not against it.** Three
   "failures" in an hour were the system teaching us its real constraints (load timing,
   context ceiling, error opacity). None were regressions; all became hardening.
4. **An error message that hides the upstream message converts a one-parameter fix into a
   debugging session.** Surface the provider's words, truncated, always.
5. **Processes that supervise other processes lie about being dead.** Kill trees and verify
   by resource (the GPU memory was the truth; the process list was the alibi).

---

## Next moves

- **The trend lens for Ask's trend zoom** — the slice serializer (`fec78da`) and the night
  lens's encounter-nesting pattern make this mostly assembly; it's the last markdown-era
  zoom in AskPanel.
- **Per-healer coverage as composable evidence** — the operator asked; the data exists
  in-process (`CoverageTimeline` per-healer frames); needs a summary block in the cache + a
  serializer. Half-session.
- **`-Context` default in `Start-SecondB70LlamaServer.ps1`** (the b70tools repo): 4096 is
  too small for multi-slice contracts; tonight ran 32768 ad hoc.
- **Provider list + Setup UI** — ADR-0010's named upgrade path, when a second simultaneous
  provider matters.
- **Remaining ghosts need design, not plumbing** (moments, mechanics, phase-reached,
  reconciled pull, raw/replay substrates) — a Tempo-side conversation first.
- **The Rack design prompt → brief** remains the queued design session.

---

## Acceptance gates met

- [x] Ask/Explorer provider configurable (`ollama`|`openai`), default unchanged; verified
      via mock + live B70; dev and prod take the same path
- [x] First live B70 inference through the product — operator-observed streaming, "works great"
- [x] Night lens end-to-end: structured emit byte-safe beside the markdown, composer at the
      opening prompt, fallback for old vintages
- [x] Over-time slices live (Reconciled encounter + Trend window), zero new endpoints,
      eleven-object contracts with stable digests
- [x] vitest 73/73 · xUnit 68/68 · every claim probed at the layer it lives on
- [x] **Phase-2 retro's deferred gates closed by the operator on the 06-06 raid night:**
      wipe verdict in a real contract, diff compare exercised, streamed investigation observed
- [x] Three field-found failures fixed forward (detection retry, error surfacing, context
      sizing documented)
- [ ] Trend lens for Ask's trend zoom — next session (slice shipped, lens pending)
- [ ] Per-healer coverage as evidence — offered, queued by the operator's coverage question
