# Retro: Ask grounds at a chosen zoom — the box-score boundary

*Ask was hardwired to one night's box score. This stretch widened it to a zoom family
(night / a boss's career / recent form) without a rebuild, extracted the Ask experience into
a reusable panel, planted it at the Pipeline's terminus, and paid down the test debt the Shape
retro flagged. The product's healing question — "are we getting better at this boss?" — finally
has a place to live.*
*Date: 2026-06-07 · Scope: 9f72adf..d50188b (8 commits, since the Shape retro)*

---

## What shipped

The headline is the **Ask zoom-grounding arc**. Ask had been welded to a single night's box
score since Leopard Zero — which made it structurally unable to answer the one question the whole
product exists to heal: *are we getting better at this boss?* Three reviewers landed on the same
verdict — **right default, wrong ceiling**: the night box score is the correct front door, but Ask
needed to reach up a zoom. The fix widened Ask to a *zoom family* without any engine rebuild,
because the cross-night substrate already existed (`CareerProjection`, the same fan-in the Roster
uses). `b042a9c` added the **career arc** zoom (`CareerSummary.cs` +101, `/api/career-summary`) and,
critically, rewrote the grounding contract in `prompt.js` to guard **scope, not just numbers** — a
night-scoped evidence set now declines a multi-week-trend question and points to Trends, instead of
presenting one night's slope as if it were the whole arc. `dab461a` then extracted the whole Ask
experience into `AskPanel.jsx` (+173), lifted provider/model detection to `App.jsx` (one detect per
load, shared across surfaces), thinned `LeopardTab.jsx` (−95 → just a provider bar + `<AskPanel>`),
and added the third **recent-form** zoom (rule-row deltas + the followership/entropy/peak-speed
coordination signals the box score and career arc don't carry, rendered client-side from the trends
artifact).

Two surfaces got composition work off the back of that extraction. `8adf7ec` made the **Pipeline**
tab open to the flowmap overview with a dashed hint instead of auto-drilling into the Trim collapse —
less overwhelming cold. Then `d50188b` planted the reusable `AskPanel` at the **Pipeline's
terminus**: the "Box score → Ask" node now renders the real Ask inline (night zoom, `allowZoom=false`),
with a visible dashed **boundary** and the load-bearing honesty line — *"the pipeline guarantees the
numbers going in, not the words coming out."* The node *feeds* the model; it does not *emit* the
answer. The Ask tab stays the front door (a link out remains) — additive, not a move.

Bracketing the Ask work: **Shape v2a** (`22f5c86`) shipped a within-night "all N attempts" overlay
(client-side sum of the per-pull normalized grids, zero backend change), then Shape was formally
**closed at v1 + v2a** (`2ab44bb`) with the all-time career overlay *parked* — normalized-space
aggregation washes out across nights/rooms; a true career signature needs Tempo raw
pre-normalization positions, a separate effort. `2a8e46f` shipped the **Setup nudge** for unparsed
logs (the missing-fights fix the Shape retro flagged as offered-not-built). And `c84f1fb` **paid the
test debt**: `ShapeArtifactTests` (+122, density invariants + career-scoped wkdelta) and
`TrendsArtifactTests` (+44, the v2 windowed shape) took the suite from 5/5 to **9/9**.

That closes out all three items the Shape retro deferred — tests, the ADR log (0001/0002 landed at
the anchor commit), and the new-logs nudge — inside this single stretch.

---

## Engineering Lead perspective

The architecturally interesting move is that **Ask got far more capable while the engine got zero
new math**. The career arc is the Roster's data (`CareerProjection` fan-in) re-rendered as grounding
text; recent form is the trends artifact re-rendered client-side. The only new C# was
`CareerSummary.cs` — a text projection over already-computed career inputs, served at
`/api/career-summary`. Everything else was composition. That's the "reflection surfaces" seam paying
off a fourth time: the substrate is rich enough that new product value is a rendering decision, not
an engine change.

The `AskPanel` extraction is the cleanest refactor of the stretch and it earned its keep
immediately — the moment Ask existed as a self-contained panel with an `allowZoom` prop and an
`AbortController`, planting it at the Pipeline terminus (`d50188b`) was nearly free. Lifting
provider/model to `App.jsx` killed the per-tab-mount re-detection that LeopardTab used to do, so
both Ask surfaces now share one detect-and-warm-model pass (the `/b70/`, `/mistral/`,
`qwen2.5:14b`… preference ladder, biased toward an already-loaded model). The stream aborts on
unmount and on a new ask — no orphaned token streams when the user switches zoom mid-answer.

The load-bearing invariant held throughout: **displayed evidence is byte-identical to what's sent
to the model.** All three zoom branches in `AskPanel`'s evidence effect build one string and use it
for both the `<pre>` and `buildMessages`. The recent-form text in particular (`trendSummaryText`)
is computed once and shared — there's no path where the user sees one thing and the model reads
another. The new scope-guard in the system prompt is the conceptual upgrade: grounding used to mean
"don't invent numbers"; it now means "don't answer outside your zoom," with the prompt declaring its
`scopeLabel` and explicitly told to redirect cross-scope questions.

Debt status, honestly: the test gap the last retro called "three deep" is now mostly paid — Shape
density invariants, career-scoped wkdelta, and the trends v2 shape all have guards (9/9). What's
still uncovered: the **IPv4 proxy smoke** (needs live Ollama, stays a manual/integration check) and
the **Ask zoom plumbing itself** — there's no test asserting that night-zoom evidence equals the box
score or that the scope-guard prompt actually carries the right `scopeLabel`. The grounding
invariant (display == send) is load-bearing and currently rests on code-reading, not a test. The
dead v1-shape fallback in `TrendsTab` is still there.

---

## Project / Program Manager perspective

This stretch did something the last two didn't: it **closed its predecessor's deferrals**. The
Shape retro left three open gates — automated tests, the ADR log, and the new-logs nudge. All three
landed here (`c84f1fb`, the 0001/0002 ADRs at the anchor, `2a8e46f`). That's healthy — debt that
gets named in a retro and paid in the next stretch instead of compounding.

Scope reality: the Ask zoom arc was the planned spine (steps 3, 5a, 5b of a "zoom plan" referenced
in the commits), and it landed cleanly in sequence — career zoom, then the extraction + recent-form,
then the terminus. The unplanned-but-valuable work this time was lighter than the last two retros
(no surprise data bug, no missing raid) — the corpus is now well-understood, so the work was mostly
the planned feature plus two composition polish items (Pipeline overview-first, the terminus Ask).
That's a sign the product is maturing past the "every session surfaces a data surprise" phase.

Risk surface: one real risk retired — Ask being **scope-blind** (presenting a night's slope as the
whole arc) is now guarded at the prompt level *and* given the right zooms to redirect to. New risk,
small: the Ask zoom plumbing has no automated guard, so a regression in the display-==-send invariant
or the scope-label wiring would ship silently. Deferred with reason: **push** (local `master` is now
~12 commits ahead of origin — operator's call); the **proxy smoke** (needs live Ollama in CI, not
worth the harness yet); the v1-shape dead-code cleanup (cosmetic).

Forward dependency: the next surface — **the Rack** (the authoring surface) — got a design *prompt*
this session (`docs/rack-design-prompt.md`, untracked). It's deliberately a prompt, not a brief: its
section 5 open questions (where on the tune→toggle→author ladder is v1; what's the unit of
manipulation; how far with zero Tempo changes) gate the whole build. That's the right artifact for
where it is — the Rack is a genuine architectural fork (it needs a user-extensibility seam Leopard
doesn't have), not another exhibit on the existing seam.

---

## QA / Verification perspective

Verification followed the now-established two-layer discipline. The **artifact layer** was checked
against real data and recorded in the commits: the career zoom used the arc (Midnight Falls
improving 22% → 7% over 55 attempts / 3 nights); the night zoom *declined* the cross-night question
and pointed to Trends — i.e. the scope-guard was verified to actually fire, not just assumed. The
**pixel layer** (does the inline terminus Ask paint, does the boundary read right) is the operator's,
per the standing division of labor.

The test debt payment is the durable QA win. `ShapeArtifactTests` now pins the invariants that were
resting on eyeball: career-scoped wkdelta contrasts across nights, the one-sided frame leaves the
measured kill columns null (Best progress is definitionally 0, not a measured mean), density carries
per-pull selector metadata, and the busiest normalized cell == 1.0. `TrendsArtifactTests` pins the
v2 windowed shape (windows keyed 4/6/8/10, default 6). 9/9 green. These are exactly the surfaces the
real-data bugs hid in last stretch — now they have guards.

The honest gap: there's **no test for the Ask zoom layer itself**. The display-==-send invariant is
the single most important correctness property in the product (it's the whole grounding claim), and
it's currently verified by reading `AskPanel.jsx` and trusting that all three zoom branches feed
`buildMessages` the same string they render. A small unit test — "for a given night, the night-zoom
evidence string equals `getBoxscore(night)`" and "the recent-form text is built once and identical
across display and send" — would move the product's core claim from code-reading to a guard. Named
as the next test debt. The proxy smoke remains a manual check (needs live Ollama), as the last retro
noted.

Transferable pattern worth naming: **verify that a guard fires, not just that the happy path
works.** The valuable check on `b042a9c` wasn't "career zoom shows the arc" — it was "night zoom
*refuses* the cross-night question." A grounding contract that only ever gets asked in-scope
questions is untested; the refusal is the feature.

---

## Operator perspective

*(first person — Derek)*

The thing I kept feeling this stretch is that the product finally answers the question I actually
have. "How'd we do last night" was always answerable — but "are we *getting better* at this boss"
is the question that makes someone stick with a guild through a progression wall, and Ask just
couldn't see it before. Widening it to the career zoom without rebuilding anything was the right
kind of cheap: the data was already there from the Roster, we just hadn't pointed Ask at it.

The call I'm glad I made was insisting on the **scope guard**, not just the new zoom. It would have
been easy to add the career view and call it done — but then a night-scoped Ask would happily
hand-wave a trend question, present one night's slope as the arc, and quietly become a worse
ChatGPT. Making the prompt *decline and redirect* when the question reaches past its evidence is the
honest version. That's the whole product thesis — grounded in your real data, or it's nothing.

The **boundary at the Pipeline terminus** is the bit I'm quietly proudest of. "The pipeline
guarantees the numbers going in, not the words coming out" — that's the most honest sentence in the
whole app. The exhibits all teach how the data is computed; the terminus is where I get to say,
explicitly, *and here's exactly where the deterministic machine stops and a model starts talking.*
Drawing that line visibly, instead of blurring it, is the kind of honesty the uncle would want.

What felt good: this was a *calmer* session than the last two. No missing raids, no thin-data bugs
ambushing me — I knew the corpus, the work went in the order I planned, and the test debt I'd been
carrying got paid without me having to nag for it. The Rack prompt at the end is me looking up at the
big one — the authoring surface, the actual destination — and I deliberately wrote it as questions,
not answers, because I don't want to pretend I've decided where on the tune→author ladder v1 lives.

---

## How we worked together (human ↔ AI)

### What worked well

- **"Right default, wrong ceiling" came out of a three-reviewer framing and reshaped the feature
  without a rebuild.** Instead of "should Ask see careers?" (yes/no) the question became "what's the
  right *default* zoom and what's the right *ceiling*?" — which preserved the night box score as the
  front door while widening the reach. That framing is why the change was additive, not a teardown.
- **The scope-guard was treated as part of the feature, not a nicety.** When the career zoom went in,
  the same commit (`b042a9c`) rewrote `prompt.js` to guard scope — the AI didn't ship the new
  capability and leave the old prompt to mis-handle it. The grounding contract evolved *with* the
  surface.
- **Extract-then-reuse paid off within the same session.** `AskPanel` was extracted in `dab461a` and
  planted at the Pipeline terminus in `d50188b` — the refactor's value was proven immediately by the
  next commit, not asserted as future-proofing.
- **Debt named in the last retro got paid in this one.** Tests, the ADR log, and the new-logs nudge
  were all Shape-retro deferrals; the next session picked them up off the retro's "next moves" and
  closed them. The retro corpus is doing its job as a worklist.
- **The display-==-send invariant was held across all three new zoom branches** — every branch
  builds one evidence string for both the panel and `buildMessages`. The AI didn't introduce a
  shortcut where the model reads something the user can't see.

### What didn't

- **The Ask zoom layer shipped without a test for its core invariant.** The product's whole claim is
  "the model reads exactly the evidence you see," and that property — across three zoom branches — is
  still verified by reading the code. The stretch paid Shape/Trends test debt but opened a thinner
  new gap on the most load-bearing surface.
- **Push keeps deferring.** `master` is now ~12 commits ahead of origin across multiple retros. Each
  retro lists "push" in next-moves and it carries to the next. Not a problem yet (solo, local), but
  it's a standing item that never closes.
- **The Rack design prompt is untracked and uncommitted** — valuable forward-thinking work
  (`docs/rack-design-prompt.md`) sitting outside version control. Easy to lose; should be committed
  even as a prompt-stage artifact.
- **"Steps 3 / 5a / 5b of the zoom plan" are referenced in commits but the plan doc itself isn't in
  the repo** (or wasn't surfaced) — the commit messages assume a plan the retro reader can't see.
  Plans that gate commit sequencing should live as a doc, not just in the operator's head.

### Patterns to repeat

- Frame a capability question as **default vs ceiling** when the answer isn't binary — it tends to
  produce additive designs instead of teardowns.
- Evolve the **grounding contract alongside the surface** — a new zoom/scope means a new prompt
  guard, in the same commit.
- Extract a component the moment a second use site is imminent, and prove the extraction with the
  next commit (don't extract speculatively).
- Pull the previous retro's "next moves" into the current session's worklist — the retros are a
  backlog, use them as one.

### Patterns to change

- **Test the invariant that IS the product.** When a feature touches the display-==-send grounding
  property, add a guard in the same stretch — that property earns a test more than any density
  invariant does.
- **Commit forward-thinking design artifacts** (briefs, prompts) when they're written, even at
  prompt stage — don't leave the next surface's thinking untracked.
- **Write the plan doc when commits start referencing plan steps.** If a commit says "step 5b," step
  5b should be readable in a doc in the repo.

---

## Lessons learned

1. **The most capable feature change can be a rendering decision, not an engine change.** Ask gained
   two new zooms with ~100 lines of new C# (a text projection over existing career data) because the
   substrate was already rich. When the data layer is right, product value is composition.
2. **Grounding means scope, not just numbers.** "Don't invent figures" is the easy half; "don't
   answer outside your evidence's zoom" is the half that keeps a grounded assistant from quietly
   becoming an ungrounded one. The refusal is the feature — test that the guard *fires*.
3. **Draw the human/AI boundary visibly.** The Pipeline terminus says out loud where the
   deterministic machine stops and the model starts ("numbers going in, not words coming out").
   Honesty about that line is more valuable than hiding it behind a seamless UI.
4. **Extract on the second use, not the first guess.** `AskPanel` was extracted exactly when a second
   call site (the terminus) was real and imminent — and the next commit proved it. Speculative
   extraction is debt; just-in-time extraction is leverage.
5. **A retro's "next moves" is a backlog.** Three Shape-retro deferrals closed this stretch because
   they were on a list someone read. Retros compound only if the next session mines them.

---

## Next moves

- **Push** — local `master` is ~12 commits ahead of origin (`git push origin master`). Standing item
  across three retros now; worth just doing.
- **Commit the Rack design prompt** — `docs/rack-design-prompt.md` is untracked. Land it (even as a
  prompt-stage artifact) so the next surface's thinking is in version control.
- **Test the Ask grounding invariant** — a unit/component test that night-zoom evidence equals
  `getBoxscore(night)` and that the recent-form text is identical across display and send. The
  product's core claim deserves a guard. Tests live in `D:\work\leopard\src\leopard-host.Tests`
  (C#) — the display-==-send property may want a small `leopard-web` test harness, which doesn't
  exist yet (decision for the operator).
- **Iterate the Rack prompt → brief** — answer section 5's gating questions, especially *where on
  the tune → toggle → author ladder v1 lives* and *how far with zero Tempo changes*. This decides
  whether the Rack is a near-term surface or a next-quarter architecture project.
- **ADR-0003 (proposed)** — Ask grounds at a chosen zoom; the prompt guards scope, not just numbers.
  Draft below; promote to Accepted when reviewed.
- **Plan + README catch-up** — feature-order still frames Ask as box-score-only; README's Leopard
  line is single-zoom. Proposed diffs below.

---

## Acceptance gates met

- [x] Ask widened to a zoom family — night (default) / career arc / recent form (`b042a9c`, `dab461a`)
- [x] Grounding contract guards scope, not just numbers — night zoom declines cross-night questions (`b042a9c`)
- [x] AskPanel extracted, provider/model lifted to App, LeopardTab thinned (`dab461a`)
- [x] Ask planted at the Pipeline terminus with a visible boundary, additive to the Ask tab (`d50188b`)
- [x] Pipeline opens to the overview, drills in on click (`8adf7ec`)
- [x] Shape v2a — within-night cross-attempt overlay (`22f5c86`); Shape closed at v1+v2a, career overlay parked (`2ab44bb`)
- [x] Setup nudge for unparsed logs — the Shape-retro missing-fights fix (`2a8e46f`)
- [x] Test debt paid — Shape density + career wkdelta + trends v2 shape, 9/9 (`c84f1fb`)
- [x] ADR log live — 0001 (cache versioning) + 0002 (career-scoped wkdelta) landed
- [ ] Ask grounding invariant has an automated guard — deferred, the new test gap
- [ ] Pushed to origin — deferred (~12 ahead)
- [ ] Rack design prompt committed — deferred (untracked)
- [ ] ADR-0003 (Ask zoom / scope-guard) landed — proposed below, awaiting review
