# Retro: Explorer phase 2 — the ghosts light up

*One evening session took the Explorer from four live knowledge objects to nine: three new
host endpoints surfacing the last unexposed RaidUI ports, two near-free flips off data already
served, and a latent formatting bug caught by writing the tests. Plus the smoothest session
open yet — `/sup` to first commit-worthy code in under an hour.*
*Date: 2026-06-11 (evening) · Scope: `58d1fe3` + `d267c39` + this retro commit*

---

## What shipped

**The debt payment** (`58d1fe3`, committed before this session opened): the `buildCareerLens`
vitest suite — the item five retros had carried. Closed before phase 2 started; the morning
retro's "20-minute task with no excuse" framing held.

**Explorer phase 2** (`d267c39`, 14 files, +918): every ghost with existing C# went live.
Host side: `CoverageTimeline.BuildJson` with a new `ToSeconds` per-second downsample (the
cache carries chart-resolution series + summary + snaps; the per-frame model stays in-process
for the classifier — ADR-0009), `FormationSegments.BuildJson` (segments + the phase story),
and a new `ClassifyArtifact.cs` running the full v2c classifier per pull with a
**verdict-or-explicit-reason** contract — "kill - kills are not classified" instead of
silence. `Program.cs` gained three cache paths, the parse-time regeneration wiring, and
`GET /api/coverage|segments|classify`.

Web side: five registry flips — **Coverage timeline**, **Formation segments**, **Wipe cause**,
**Movement meters** (a documented `meters` pseudo-api that rides the affinity payload, zero
new fetches), and **Shape** (the existing density grid serialized as hotspot/concentration
stats). Each flip carries a slice serializer with explicit-absence lines, a Properties
preview (quality line chart with snap markers, formation strip, verdict banner, distance
leaderboard, density mini-heatmap), real slice options where cheap (coverage `rep`, classify
`rep`, meters `scope`/`agg` — all REAL_KEYS-honest), and golden tests.

**The catch**: writing the meters test fixture exposed a latent phase-1 bug in `contract.js`'s
`fmt()` — at `digits=0`, a non-integer rounding to a trailing zero got the zero stripped
(`1199.6` → `"1200"` → `"12"`). Real-data prompt slices could have read "coverage avg 8%"
where the truth was 80%. Fixed (strip only after a decimal point) and pinned with the exact
regression value.

Tests: vitest 49 → **57**, xUnit 62 → **67**. Verified live: the host re-parsed the
2026-06-09 night and served all three new endpoints (coverage 24 KB / segments 12.7 KB /
classify 1.2 KB, all 200); classify correctly declined all three kills with stated reasons.
Wipe verdicts proven at the xUnit layer on the 8-wipe Belo'ren fixture. ADR-0007 and
ADR-0008 promoted to Accepted; ADR-0009 extracted; `docs/feature-order.md` and `README.md`
updated in this retro commit.

---

## Engineering Lead perspective

The headline validation is ADR-0007 surviving its first real extension untouched. Phase 1's
bet — full ghost taxonomy in the registry, compiler dispatching on `entry.api` — meant phase 2
never modified the envelope, the digest rules, or the tree component. Five objects went live
through exactly the path the design promised: flip the registry entry, add a serializer case,
add a preview. The one place the abstraction needed a judgment call was Movement meters,
which has no endpoint of its own — the `'meters'` pseudo-api key is honest about that
(documented in the registry header, availability mirrors affinity, tested), which beats both
a redundant fetch and a silent special case.

ADR-0009 is the genuinely new decision: caches store what the UI consumes (seconds), full
resolution recomputes in-process where needed (the classifier's frame-level coverage reads).
The numbers vindicate it — 24 KB instead of megabytes for the coverage cache. The cost,
duplicate `Compute` walks between the coverage and classify builders at parse time, follows
the established each-builder-walks-independently convention and is invisible against a
night's parse.

The `fmt()` find deserves its own note: the bug was *not* found by reading the code — it was
found because building a realistic test fixture (a tank who ran 1199.6 yd) produced an
impossible expected value. Tests written against invented-but-plausible data audit the code
they pass through. Debt ledger: REAL_KEYS in `ExplorerProperties.jsx` is still a hand-synced
mirror of the serializer branches (now 9 entries — the drift risk named in ADR-0007 grows
with each flip), and the remaining seven ghosts are not plumbing — they need endpoints and
design that don't exist.

---

## Project / Program Manager perspective

This is what a pre-paid plan looks like. The phase-2 plan was written last session, *after*
the design existed and with reuse claims verified against real files — so this session opened
with `/sup`, confirmed the plan's facts still held, and executed. Zero clarifying questions
to the operator between "onward to phase 2" and the verification report. Compare the
phase-1 session, which burned its first hour on a dead cloud plan and a re-paste — the
artifact discipline (plan docs that survive sessions) is what converted that loss into this
session's speed.

Scope tracked the plan's phase-2 section exactly: the "C# modules exist and are tested —
plumbing only" claim was true, and the "near-free flips" (meters, Shape) were indeed
near-free. Schedule: roughly two hours from briefing to verified endpoints, plus the
launch-and-look. The session also closed the morning retro's two clerical items (ADR
promotions came with evidence: 0007 survived an extension, 0008 survived every launch).

Risk surface: nothing new introduced — phase 2 added zero dependencies and reused every
established pattern. Still open and now touching three surfaces: the `/ollama` proxy target
(tonight it was worked around by *starting Ollama*, which is fine for testing but is not the
dual-B70 substrate the product intends). Still gated on a raid night: a wipe verdict through
the live endpoint and the diff compare picker.

---

## QA / Verification perspective

Covered this stretch: 5 new xUnit (the `ToSeconds` averaging + empty-input contracts, and
three fixture-night shape tests in `NightArtifactTests.cs` — notably `Classify_artifact`
asserts an 8-wipe real-log fixture produces at least one verdict, so "classify works on real
wipes" is *proven*, just not yet observed through the HTTP layer). 8 new vitest: golden
serializations for all five new slices, the rep/scope/agg toggles changing bytes, the
called-wipe coaching gate verbatim, explicit-absence lines for every artifact, nine-slice
contract digest stability, and the `fmt` trailing-zero regression pin.

Verified by independent evidence: the host was rebuilt (npm → dotnet pairing honored),
launched, the 2026-06-09 night re-parsed (regeneration triggered by the missing new caches,
as designed), and all three endpoints probed with payload inspection — including confirming
the classify artifact's honest declines on an all-kill night. The launch handed to the
operator was the same exe, probed for health *and* the fresh bundle hash before handover —
the morning retro's "verify the exact artifact you hand over" lesson, applied. The operator's
own pass: "no obvious errors."

Not covered, accepted: a wipe verdict and the diff picker through the live UI (the parsed
night is all kills — raid night required); a streamed investigation was made *possible*
tonight (Ollama up, proxy 200) but no streamed answer was observed from this side of the
screen — if the operator ran one, that gate is his to check off; React components remain
manual-pass by repo convention.

---

## Operator perspective

I opened with `/sup` after a day away and the briefing told me exactly one thing I needed:
phase 2 was planned, ready, and everything else wanted either a raid night or a design
session. "Onward to phase 2" was the whole instruction, and the next thing I had to actually
decide was nothing — the plan we made after the mockup landed last session had already made
the decisions.

The thing I keep noticing is the ghosts were never decoration. I shipped a tree where
two-thirds of the entries were 45%-opacity promises, and tonight five of them turned on
without the tree moving. That's the registry pattern paying rent. And the classify artifact
saying "kill - kills are not classified" instead of returning nothing is exactly the
control-legibility bar I set in the refinement phase — the system tells you why it's quiet.

The fmt bug is the one that would have bitten me in front of my uncle. "Coverage avg 8%" in
a prompt slice is the kind of wrong that looks like a real number. It got caught because the
tests use data that looks like my data. I started the app, clicked around, no obvious errors
— and I know that's the shallow layer; the raid-night items are on me to exercise Tuesday.

---

## How we worked together (human ↔ AI)

### What worked well

- **The /sup → /retro contract closed its first full loop.** The morning retro's "Next moves"
  became the evening's `/sup` briefing became "onward to phase 2" — re-entry cost was one
  typed word. The skill pair is doing what it was built for: the retro corpus is functioning
  as session-to-session memory.
- **A plan written after the design, with verified facts, made the build question-free.**
  Zero AskUserQuestion calls this session. Every fact the plan asserted (diff signature,
  meters embedded in affinity, fixture availability) checked out against the code on re-read.
- **Test fixtures as audit.** The 1199.6-yd meter row was invented to test the new serializer
  and instead caught a phase-1 bug in shared formatting code. The AI flagged it, fixed it,
  and pinned it without being asked — the right autonomy level for a correctness bug.
- **The handover lesson from the morning retro was applied, not just recorded.** Both app
  launches this session probed health + the served bundle hash before telling the operator
  to look; the second launch also probed the /ollama proxy route end-to-end (the exact path
  Run Investigation uses) before claiming the investigation was testable.
- **Honest declines surfaced as a feature.** When verification showed classify returning
  null for every pull on the M+ night, the AI reported it as designed behavior with the
  reason strings as evidence — not as a problem to paper over — and separately proved the
  verdict path on the wipe fixture.

### What didn't

- **The AI stopped the verification host, then the operator immediately wanted to look.**
  Cleanup discipline (kill what you started) collided with the obvious next step (the
  operator will want to see this). One wasted relaunch. Cheap, but predictable.
- **Ollama needed a manual start because the proxy mismatch is still unresolved.** Third
  session in a row where the 11434-vs-8080 gap cost a step. It worked — but "start Ollama so
  the product works" is a workaround being normalized, and the dual-B70 substrate sits idle.
- **The property-inventory id folding slipped again.** Named in the morning retro's next
  moves, named in the evening briefing as "ready now," still not done — and the live-id
  count it would document grew from four to nine tonight.

### Patterns to repeat

- End sessions by making the *next* session's plan while the design facts are still in
  context — this session's speed was manufactured last session.
- Build test fixtures with realistic magnitudes; they audit shared code paths for free.
- Probe the exact route a feature uses (the /ollama proxy through the host, not just
  Ollama directly) before declaring it testable.
- Registry/ghost patterns for phased UI: ship the taxonomy, flip entries.

### Patterns to change

- When verification ends inside a session the operator is active in, leave the app running
  and say so — or ask. Stopping it is only right at session end.
- The proxy target should become config (the `liveInferenceUrl` pattern already exists in
  `LeopardConfig`) before the workaround calcifies further.

---

## Lessons learned

1. **A plan made after the design, against verified code, converts the next session into
   pure execution.** The expensive part of planning is fact-checking; do it once, while the
   facts are loaded.
2. **Ghost entries are a contract with your future self, and they pay out.** Five flips,
   zero tree changes, zero compiler changes — the cost of carrying the taxonomy early was
   one screen of registry data.
3. **Caches should store what consumers render; recompute the rest.** Seconds vs frames was
   a 100x payload difference with zero consumer loss (ADR-0009).
4. **Absence needs a reason, at every layer.** The classify artifact's
   verdict-or-explicit-reason shape extends the explicit-absence discipline from prompt text
   down into cached artifacts — and it made the all-kill verification night *legible* instead
   of confusing.
5. **Realistic test data is a code auditor.** The fmt bug survived 20 phase-1 tests because
   their numbers were round; it died on the first fixture with a messy real-world magnitude.

---

## Next moves

- **Raid night (the standing gates):** a wipe verdict + coverage pattern through the live
  endpoint, the diff compare picker on a multi-pull boss, and a streamed investigation
  observed end-to-end. One re-parse after Tuesday lights all of it.
- **Make the /ollama proxy target configurable** (the `LiveInferenceUrl` pattern in
  `LeopardConfig` is the template) and point it at the vllama facade (:8090) / llama-server
  (:8080) — retires the three-session-old mismatch and frees the workaround.
- **Fold the nine live knowledge-object ids into `docs/property-inventory.md`** — third
  listing; the namespace it documents doubled tonight.
- **Remaining ghosts need design, not plumbing:** Moment (view), Mechanic trigger/fired,
  Phase reached, Reconciled pull/encounter, Raw event, Replay, Followership breakout — each
  wants a Tempo-side conversation before a host endpoint.
- **Night and trend lenses** (structured C# emit) and the **Rack design prompt → brief**
  remain the queued design sessions per `docs/feature-order.md`.

---

## Acceptance gates met

- [x] All three planned endpoints live (`/api/coverage|segments|classify`) with parse-time
      caches and re-derive-on-missing wiring
- [x] Five registry flips with serializers, previews, real-vs-annotation slice options, and
      REAL_KEYS honesty maintained
- [x] Meters and Shape flipped with zero new host work (the "near-free" claim held)
- [x] vitest 57/57, xUnit 67/67; wipe verdicts proven on the 8-wipe real-log fixture
- [x] Endpoints verified against the real 2026-06-09 night; classify's honest declines
      observed; npm → dotnet build pairing honored; handed-over launch probed (health +
      bundle hash + proxy route)
- [x] ADR-0007/0008 promoted with evidence; ADR-0009 extracted; feature-order + README current
- [x] Operator visual pass: nine live dots, no obvious errors
- [ ] Wipe verdict / diff picker / streamed investigation through the live UI — deferred to
      a raid night (the parsed night is all kills)
- [ ] Property-inventory id folding — slipped a second time; now nine ids, do it next session
