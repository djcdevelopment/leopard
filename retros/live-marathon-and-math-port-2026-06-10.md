# Retro: the Live marathon — between-pull insight + the RaidUI math port, and the PR #1 close-out

*One evening (2026-06-09, 19:31–22:50) shipped the seventh surface — Live, the between-pull
insight loop — and strip-mined the entire RaidUI JS analysis corpus into tested C#. The next
morning's session closed the three Windows-side gate items that had blocked PR #1 for three days
and merged the whole arc to master.*
*Date: 2026-06-10 · Scope: `0bea285..332843d` (13 commits) + this session (`eabbba8`, merge `5d95900`)*

---

## What shipped

**Live** (`0bea285`, +815 lines opening commit): `LiveSession.cs` consumes
`Tempo.Core.Ingest.FileSystemLogMonitor` + `CombatLogParser` in-process — zero Tempo changes —
to detect a pull ending while WoW is still writing the log. It assembles three-layer evidence
(the pull's typed facts + tonight's in-memory trajectory + the cached all-time career), POSTs it
to a config `liveInferenceUrl` (OpenAI-style, default llama-server `:8080` on the 2nd B70), and
has the insight ready before the operator looks. `LiveTab.jsx` is the review desk: card, evidence
disclosure (display==send, byte-identical), two named feedback axes (*useful* / *grounded*) plus
a comment. Every lifecycle event appends to `live-insight.jsonl` — the replayable eval corpus for
the critic-loop work and the file bridge discoverlay will tail. Brief committed alongside the
code: `D:\work\leopard\docs\live-insight-design-brief.md`.

**The math port** (eleven commits, `d2afa53..332843d`): the entire RaidUI reducer/diagnostics
layer, ported module by module into leopard-host on the ShapeArtifact pattern with RaidUI's
`__tests__` as parity oracle — `SignalsArtifact` (`fe58472`, the six-signal pack), `PullDiff`
(`01f6928`), `WipeClassifier` (`91481d7`, the classify.js rule tree incl. the called-wipe
"don't coach an intentional reset" prompt gate), `CoverageTimeline` (`4a42541`, per-healer
quality + snaps + named-offender attribution), `MovementAffinity` (`6d64142`) + `CoverageGaps`
(`669c7fd`), `FormationSegments` (`678b8cb`), `PlayerScores` (`3931f10`), `ParticipantMeters` +
group summary (`98a07c0`), `PullDivergence` (`332843d`). Each landed with xUnit invariant tests
and a real-log verification before the next started. Suite: 9 → **62**. The port audit is
**closed**: every RaidUI math module is now ported, superseded, or parked-with-reason.

**This session's close-out**: the three Windows-side gate items that had sat since 06-07 —
`dotnet build src/leopard-host` (clean), `dotnet test` (**62/62, run and seen**), and the
disk-to-confirm pass over real cache artifacts (`eabbba8`) — then PR #1 marked ready and merged
(`5d95900`): 44 files, +8,604 lines onto master. The disk-confirm was not ceremony: it found two
uninventoried pull fields (`ageDays`, `close`), a fifth trends rule row (`Time on encounter`),
the `maxBucket` density field, lowercase `outcome` values, and two on-disk key names that differ
from their inventory IDs (`pullId`/`endHpPct`). ADR-0005 (math-port placement) and ADR-0006
(live-loop division + jsonl bridge) extracted in this retro commit.

---

## Engineering Lead perspective

The marathon's load-bearing decision is captured in ADR-0005: the RaidUI math went into
**leopard-host artifact modules**, not Tempo, and not a JS sidecar. The ShapeArtifact pattern
(parse-time compute → per-night JSON cache → thin API) absorbed nine new modules without
strain, which is the strongest evidence yet that the pattern is right. The modules are pure —
replay frames and typed events in, POCOs out — so the "should this live in Tempo.Projections?"
question stays a cheap refactor rather than a rewrite if a second consumer ever materializes.

The port discipline held under marathon pace, which is when discipline usually breaks. Every
module carried tests derived from the RaidUI oracle before moving on (the per-commit stats show
test files landing *with* their modules, not after), and every module got a real-log check —
the affinity port recovered melee+tank as the boss-hitbox movement group on the M+ log; the
meters port's teleport detector caught a real 82 yd/s melee dash; the classifier correctly
labeled a synthetic early reset as a called wipe. Deviations from the JS are documented in file
headers (WipeClassifier's per-second signal adapter, CoverageTimeline's zero-tank guard) instead
of silently diverging from the oracle.

`LiveSession.cs` grew from 404 to 658 lines across the evening as each port wired in a new
evidence section. That's the file to watch: it's accreting evidence-assembly responsibilities
commit by commit, and at some point the evidence block builder wants the same treatment
`CanonicalContext` gave the Ask zooms — a structured, testable assembly path rather than
sections appended in sequence. Not urgent; worth naming before it's 1,200 lines.

Debt ledger: the `buildCareerLens` vitest test **still does not exist** — fourth consecutive
retro naming it. The named-offender attribution is unit-covered but has never fired on real
raid-night data (needs a real wipe with healer movement). The dead v1-shape fallback in
`TrendsTab` survives another retro. New debt: nothing structural — the marathon added tests at
a higher ratio than the codebase average.

---

## Project / Program Manager perspective

Scope reality: the evening was not planned as a marathon. The 06-09 retro was committed at
15:43 framing the career lens as the stretch's terminus; by 22:50 the same day, thirteen more
commits had shipped a seventh surface and closed an entire port audit. The retro was stale
within seven hours. That is the fastest doc-reality divergence this project has produced, and
it is worth being honest that no process would have prevented it — the operator caught fire and
built. The mitigation is exactly what happened this morning: `/sup` corroborates against git
rather than trusting the last retro, and it correctly flagged the divergence in under a minute.

PR #1 merged carrying far more than its title (`feat: CanonicalContext + vitest harness +
property inventory`). The ensure rung, the compose rung, Live, and the math port all rode one
branch and one PR. For a solo repo with no reviewers this cost nothing today; the title is now
wrong in the permanent record, and bisecting "which PR introduced X" later will point at a PR
whose description covers a quarter of its contents. The lesson is not "smaller PRs" dogma —
it's that the merge should have happened at the ensure-rung boundary when the gate items were
first listed, and everything after would have been PR #2.

The three-day gate-item lag resolved exactly as the last retro predicted it would: the items
were trivial (build: 1.84s; test: 101ms; disk-confirm: four PowerShell queries) once someone
sat down at the Windows machine. The accumulation cost was real — the branch grew 5,000 lines
on top of an unmerged base. Dependencies retired this stretch: Live no longer blocks on Tempo
work (in-process ingest proved out), and the critic-loop's eval corpus now has a collection
mechanism. New dependency surfaced: overlay delivery is now blocked on discoverlay-side work,
in a separate repo with its own build.

---

## QA / Verification perspective

This session is the first time both suites were run and seen green in the same sitting by the
agent doing the reporting: vitest 18/18, xUnit 62/62, and the host build clean — stated from
this run, not from a document. The verified-needs-a-layer rule held.

The marathon's verification pattern deserves naming as transferable: **oracle-anchored porting**.
Each C# module's tests were derived from the RaidUI `__tests__` fixtures and invariants, so
"the port is correct" reduces to "the same inputs produce the same classifications/scores the
proven JS produced," plus documented deviations. That is a far stronger claim than fresh tests
written against the new implementation's own behavior, which would happily certify a
misunderstanding. The per-module real-log check on top (affinity recovering the hitbox group,
the teleport catch) covered the gap parity tests can't: that the *adapters* feeding the math are
right, not just the math.

The disk-confirm pass found real schema drift between the inventory and the artifacts: two
pull fields nobody had inventoried, a fifth rule row, a nested density object where the doc
implied flat keys, and on-disk names (`pullId`, `endHpPct`) that differ from the inventory's
IDs. None were bugs; all were exactly the class of silent mismatch that would have bitten the
night/trend lens builders. The checklist items are now checked with findings inline —
`D:\work\leopard\docs\property-inventory.md`.

Uncovered, in priority order: `buildCareerLens` (pure function, fully specified, four retros
old); the lens-composer UI integration (checkbox → XML → model bytes); named-offender
attribution on real raid-night data (verification ladder step 5 — needs an actual raid);
the live failure path under a down inference server on a real night; IPv4 proxy smoke. The
called-wipe prompt gate produced a notable specimen: the model acknowledged the gate and
coached anyway — already captured in the jsonl as prompt-compliance evidence for the critic loop.

---

## Operator perspective

*(first person — Derek)*

The marathon happened because the port audit made it inevitable. Once the unported-math audit
existed — every module named, sized, with its RaidUI source and test oracle — each port was a
forty-minute loop: read the JS, write the C#, port the tests, check it on a real log, commit.
The audit turned an intimidating "someday port the analysis layer" into a checklist, and I
cleared the checklist in one sitting. The lesson for me is that the expensive artifact was the
*audit*, not the ports. The ports were execution; the audit was the decision.

Live is the piece I've wanted since before Leopard had a name. The between-pull window is the
highest-fidelity labeling moment this product will ever get — I just lived the pull, my memory
of it decays in minutes, and the card is ready before I alt-tab. v1 is an expert labeling
station wearing an insight card's clothes, and that's by design: the jsonl corpus is the point.

The morning was comedy: the PC rebooted for a Windows update while I slept, I woke up to a lock
screen that shouldn't have been on, and `/sup` re-entered the project cold — caught that the
retro was seven hours stale, ran both suites, and then we closed in minutes the three gate
items I'd let sit for three days. The disk-confirm I'd been treating as ceremony found five
real schema facts the lens builders would have tripped on. The gate items were never hard;
they were just unowned. Merged. Master finally carries everything.

What I'm being honest about: the lens test is now a four-retro joke. It's a pure function with
a written spec. If it isn't in the suite by the next retro, the next retro should open with it.

---

## How we worked together (human ↔ AI)

### What worked well

- **`/sup` paid for itself in one use.** The session opened with the operator absent overnight
  (unplanned reboot). The skill read the pickup doc and the 06-09 retro, refused to trust them,
  found 13 commits postdating the retro, ran both test suites for ground truth, and delivered a
  corrected briefing — including the fact that one of PR #1's three "outstanding" gate items
  (dotnet test) was already satisfied. The stale-handoff problem the skill was built for
  occurred in its worst form to date (stale in 7 hours) and the corroborate-don't-parrot
  design handled it.
- **"Read my mind" worked because the suggestion was already on the table.** The operator's
  entire instruction was "go with your suggestion." That only functions when the previous turn
  ends with one concrete, prioritized next action rather than a menu. The briefing's closing
  line — close the two remaining gate items, merge, retro — was executable verbatim.
- **The disk-confirm was executed as a real check, not a checkbox.** Four artifacts queried
  with PowerShell against the inventory's claims; findings (new fields, wrong labels, nested
  shapes, naming drift) folded back into the doc with dates and promoted confidence levels.
  The check found things, which retroactively justified it having been a gate item.
- **Oracle-anchored porting at marathon pace.** The evening's 11 port commits each carried
  tests derived from the RaidUI oracle and a real-log check before the next began. The pattern
  (audit → brief → port-with-oracle → real-data check → commit) repeated 9 times without
  quality decay — visible in the per-commit stats, where test files land with their modules.
- **Memory hygiene during `/sup`.** Two stale memory entries (the "still JS-only" audit index
  line, the "9/9 tests" build state) were caught and corrected before the briefing, so the
  next session's recall won't resurrect dead facts.

### What didn't

- **The retro went stale the same day it was written.** The 06-09 retro is accurate about
  everything it covers and silent about the seventh surface built seven hours later. Nothing
  was wrong with the retro; the gap is that a 13-commit marathon ended at 22:50 with no
  handoff note at all — the next session re-derived the marathon's shape from commit messages.
  A two-line pickup stub at the end of a late-night stretch would have been enough.
- **PR #1's title describes a quarter of what merged.** The merge should have happened at the
  ensure-rung boundary three days earlier; instead the branch became a catch-all and the PR
  record is permanently misleading. Solo-repo cost today: zero. Archaeology cost later: real.
- **The lens test was deferred a fourth time.** This session closed gate items, merged, and
  wrote docs — and still didn't spend the twenty minutes the test needs. The pattern survives
  because each session has a headline deliverable the test isn't part of. It is now the
  explicitly-named first acceptance gate of the next build session.
- **The called-wipe specimen is sitting unexamined.** The model coached through an explicit
  "don't coach" gate, the jsonl captured it perfectly, and no follow-up exists yet — no prompt
  iteration, no critic-loop issue filed. The corpus is collecting; nothing is consuming it.
- **The first draft of this retro missed the discoverlay half entirely.** The overlay consumer
  (`5614536` — InsightState + panel + smoke-verified card) shipped the same evening, in a sibling
  repo, and was even recorded in the session memory — but the retro was scoped from this repo's
  git log alone and listed overlay delivery as future work until the operator corrected it.
  Cross-repo stretches need cross-repo scope discovery: when a brief names sibling repos
  (Tempo, discoverlay), the retro should check their git logs too.

### Patterns to repeat

- Open every cold session with `/sup`; trust its git corroboration over any doc, including
  retros written hours earlier.
- End even (especially) late-night marathons with a two-line pickup note: what just shipped,
  what's first tomorrow. The 06-07 pickup doc proved the high-leverage version; the marathon
  proved the cost of the zero version.
- Oracle-anchored porting: when a proven implementation exists, derive the new tests from the
  old test corpus and document deviations in the file header. Fresh tests certify
  misunderstandings; oracle tests certify parity.
- Close the audit before starting the work. The unported-math audit converted a months-old
  intimidation into a one-evening checklist.

### Patterns to change

- **Merge at rung boundaries.** When a PR's gate items are listed, close them and merge before
  the branch accrues the next stretch. The three-day lag turned a focused PR into a 8,600-line
  omnibus with a stale title.
- **The lens test is no longer a "next move" — it's the entry gate.** The next session that
  touches `src/leopard-web` writes `lens.test.js` first, before its headline work. Four retros
  is enough.
- **Schedule a consumer for the feedback corpus.** The jsonl is accumulating specimens
  (including the prompt-compliance failure) with no reader. The critic-loop work should start
  with the specimens already in hand, not wait for a critical mass that nothing is defined to
  consume.

---

## Lessons learned

1. **The audit is the expensive artifact; the ports are execution.** A complete, sized,
   oracle-mapped inventory of unported work converted "someday" into a single-evening
   checklist. Budget for the audit; the marathon follows almost for free.
2. **Handoffs decay in hours, not days, when the operator is in flow.** The corroborate step
   (`git log` since the doc's mtime) is not paranoia — it caught a same-day divergence. Any
   re-entry ritual that parrots the last doc without checking git is worse than none.
3. **A file is a fine integration bus for a solo, single-machine product.** The jsonl bridge
   decouples three repos with zero infrastructure, gives replay for free, and carries its own
   versioning seam (`v` per line). The fancy alternative (IPC, sockets, a queue) would have
   bought nothing today and cost the evening.
4. **Evidence starvation poisons feedback loops.** The first Live draft's five-scalar evidence
   would have made every thumbs-down measure input quality, not model quality. If the grading
   corpus is the product, the evidence layer is where the engineering belongs — the model call
   is the cheap part.
5. **Gate items don't age into harder work — they age into heavier branches.** The three checks
   took minutes after three days of blocking a merge that then carried 5,000 extra lines. Close
   gates at the rung where they open.

---

## Next moves

- **`lens.test.js` — entry gate for the next web session.** Spec is written in the 06-09 retro's
  QA section; pure function, no DOM. Before any headline work.
- **Night + trend lens builders** — gate: structured C# emit alongside the `BoxScore.cs` /
  trends markdown blobs. The disk-confirm findings (5th rule row, exact labels, coherence point
  schema) are now in `D:\work\leopard\docs\property-inventory.md` ready for those builders.
- **Verification ladder step 5: a real raid night on Live.** Card pre-generated before alt-tab;
  named-offender attribution observed (or not) on a real wipe with healer movement; failure
  path exercised if the rig hiccups.
- **Start the critic-loop consumer with the specimens in hand** — the called-wipe
  prompt-compliance failure is already in the jsonl, fully replayable. See
  `D:\work\leopard\docs\live-insight-design-brief.md` §4 and the vllama substrate.
- **discoverlay overlay delivery — already shipped and smoke-verified end-to-end** (corrected
  after the operator flagged it): `D:\work\discoverlay` commit `5614536` (2026-06-09 19:37 — the
  same minute as Leopard's `d2afa53` hide cue; the bridge's producer and consumer were built in
  lockstep). `InsightState` projector over `live-insight.jsonl` (combat-aware hide, feedback
  joined by `insightId`), `build_insight_panel`, a 14-check smoke harness, and a committed
  rendered card (`snap-insight.png`) — verified synthetic encounter → pull facts → evidence →
  real inference on the 2nd B70 → card via `hud.exe --snapshot`. Still open on that side:
  feedback hotkeys (Ctrl+J/K taps), the Discord bridge one-liner, and real-raid-night validation.
- **Iterate the Rack prompt** (`D:\work\leopard\docs\rack-design-prompt.md`) — section 5's
  gating questions still decide the authoring-surface timeline.
- **Check the host `/ollama` proxy target** — memory flags it may still point at `:11434`
  (Ollama) while the rig runs llama-server on `:8080`; Live bypasses the proxy via
  `liveInferenceUrl` but desktop Ask does not.

---

## Acceptance gates met

- [x] Live v1: LiveSession on Tempo's in-process ingest, three-layer evidence, pre-generated
      insight, LiveTab review desk, named feedback axes, replayable jsonl (`0bea285`, `f8851d5`)
- [x] Overlay hide cue: `encounter started` emitted to the jsonl (`d2afa53`)
- [x] Six-signal pack ported with oracle tests + real-log check (`fe58472`)
- [x] Pull diff ported, live "vs your best tonight" evidence (`01f6928`)
- [x] Wipe classification ported incl. called-wipe prompt gate (`91481d7`)
- [x] Coverage quality model + named-healer attribution (`4a42541`) — *live firing on real
      raid-night data still unobserved*
- [x] Affinity/clustering, coverage gaps, segments, player scores, meters, divergence ported
      (`6d64142`, `669c7fd`, `678b8cb`, `3931f10`, `98a07c0`, `332843d`)
- [x] Port audit closed: every RaidUI math module ported, superseded, or parked-with-reason
- [x] xUnit suite 9 → 62, all green (run and seen this session)
- [x] PR #1 gate items: dotnet build clean, dotnet test 62/62, disk-confirm done with findings
      committed (`eabbba8`)
- [x] PR #1 merged to master (`5d95900`)
- [x] ADR-0005 (math-port placement) + ADR-0006 (live-loop division + jsonl bridge) — this commit
- [ ] `buildCareerLens` vitest test — deferred a fourth time; entry gate for the next web session
- [ ] Real-raid-night Live verification (ladder step 5)
- [ ] Night + trend lens builders (gate: structured C# emit)
