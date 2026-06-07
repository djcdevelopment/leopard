# Retro: Shape shipped — heatmap + career kill-vs-wipe

*Brief -> two-reviewer pass -> a data check that reversed the design -> build -> corpus backfill
-> and a Trends bug the real data finally exposed. The sixth surface is live.*
*Date: 2026-06-07 · Scope: 4886d05..b3bf884 (5 commits, since the last retro)*

---

## What shipped

Shape — the long-exposure heatmap plus a career-scoped kill-vs-wipe contrast — landed as Leopard's
sixth surface, on the same parse-time-artifact seam the other surfaces use (zero Tempo engine
changes). The arc was unusually disciplined: a design brief (`320d5df`), then a two-persona review
that rewrote it (`3aef269`), then the build (`ae5ebff`). Bracketing Shape were two smaller fixes —
the night picker moved above the tabs into global chrome (`ddc8afe`), and a Trends summary-number
bug that only became visible once real multi-pull data arrived (`b3bf884`).

The load-bearing decision was made *before* writing code: a 5-minute `/api/career` check reversed
the whole companion-view design. Per-night the corpus looked too thin for the kill-vs-wipe contrast
(every boss is all-kills or all-wipes on a given night), but across careers **48 of 119 bosses have
both** — Crown of the Cosmos at 2 kills / 52 wipes. So wkdelta was scoped to the career (fanned
across nights like `/api/career`), not the night. Build commit `ae5ebff`: `ShapeArtifact.cs` (+79),
`Program.cs` (+52, two `/api/shape/*` endpoints + the versioned cache), `ShapeTab.jsx` (+200),
plus api/App/styles. Verified end-to-end on real data — Midnight Falls heatmaps at ~30k position
samples, Crown of the Cosmos wkdelta showing kills at 4.8 avg deaths vs wipes at 20.25 — then the
corpus was backfilled (74/74 nights have a Shape artifact, 70/74 have at least one movement
heatmap; richest is Crown of the Cosmos P1 at 67,491 samples). Tests stayed 5/5 throughout.

A late thread: the operator noticed "tonight's raid" was missing. It was — the night's 628 MB log
(`WoWCombatLog-060626_175859.txt`) had never been parsed (the backfill only re-touched the 74
already-parsed nights). Parsing it added six fights (five Heroic kills + an 11-pull Midnight Falls
progression). Surfacing that boss's 26-pull career on the Trends tab is what finally exposed the
summary-number bug fixed in `b3bf884`.

---

## Engineering Lead perspective

The build was almost mechanical because the seam is now well-worn: `ShapeArtifact` mirrors
`TrendsArtifact` (compute at parse time, cache JSON keyed by mtime, serve to a thin React tab), and
the cache-version suffix pattern (`.shape.v1.json`, after `.trends.v2.json`) plugged straight into
the existing "regenerate if ANY artifact missing" guard in `Program.cs`. The one genuinely new
architectural shape is the **split scope**: density is per-pull and caches cleanly per night, while
wkdelta is career-scoped and *cannot* be a per-night cached artifact — it's computed live at
`/api/shape/wkdelta` from the fanned career-input artifacts, exactly the fan-in `/api/career`
already does. That asymmetry is the most important thing to remember about this surface.

Code quality held. The heatmap renders crisp 32x16 cells (no smoothing — honest to sparse data),
self-scaled per pull, with thin-data and no-movement empty states and a first-class one-sided frame
for bosses never killed. Those weren't afterthoughts; they came straight out of the review and were
in the brief before a line of `ShapeTab.jsx` was written.

Technical debt, named honestly: **no automated tests cover Shape** (density invariants, the
career-scoped wkdelta endpoint), nor the IPv4 proxy or the trends v2 artifact shape — all still
resting on manual + screenshot verification. The legacy v1-shape fallback in `TrendsTab` remains
dead code. And `ShapeArtifact` serializes every pull's full 512-cell grid into one JSON; fine at
current sizes (KB), worth watching if pull counts or grid resolution grow.

---

## Project / Program Manager perspective

Scope landed as planned and then some — Shape v1 (both views, not the de-risked heatmap-only
fallback), plus two unplanned fixes that real usage surfaced. The schedule reality worth noting:
the *building* was fast; the *deciding* (brief + review + data check) was where the time and the
value concentrated. That's the second retro running where the unplanned, usage-driven work
(last time: two desktop bugs; this time: tonight's-raid-parse + the Trends bug) was as important as
the planned work. The lesson is compounding: plan covers what we know; using it on real data covers
what we don't.

Risk surface: one real risk retired (wkdelta being empty-on-every-night — fixed by career scope,
proven against 48 mixed careers), one latent risk retired by accident (the missing tonight's-raid
data, which would have read as "Shape is broken" if the operator hadn't looked). New risk
introduced: the test gap is now three features deep (Shape, proxy, trends-shape) with no automated
coverage — that debt should be paid before the next surface.

Deferred, with reasons: push (4 commits ahead of origin — operator's call on timing); automated
tests (not blocking, but accumulating); the ADR log (proposed twice now, still unstarted); a
"new logs detected — parse them" nudge on Setup (offered, not built — would have prevented the
missing-fights confusion). Dependency note: Shape hard-depends on advanced combat logging being on
(position data); 4 of 74 nights lack it and correctly fall to the empty-state.

---

## QA / Verification perspective

Verification was deliberately two-layered, applying last retro's hard-won lesson ("verified needs a
layer"). The **artifact layer** was checked by me against real data: density returned 30,347 samples
on a Midnight Falls pull with `max(cells)==1.0`, all cells in [0,1], `sum(rawCounts)==totalSamples`,
512 cells; the career wkdelta returned a populated two-column contrast for Crown of the Cosmos
(5 kills / 4 wipes) and the one-sided frame for the all-wipe bosses. The **pixel layer** was held
explicitly for the operator — I did not claim Shape "worked" until he said "looks good," because the
AI cannot see whether the heatmap actually paints.

The strongest verification move was upstream of any test: the `/api/career` data check that ran
*before* building. It falsified the per-night assumption and re-scoped wkdelta — catching a design
error that no unit test would have, because the unit test would have faithfully verified the wrong
(empty) behavior. The two-persona review (V&V + statistical-honesty) was the other high-value check;
it corrected a factual error in my own reasoning (see below) before it reached code.

Regression surface and gaps: the Shape endpoints, the proxy, and the trends v2 shape have **no
automated guard** — the corpus backfill (74/74) and the operator's eyeball are currently the
regression suite. That's acceptable for a just-shipped surface on a solo project but should not
persist. The named transferable pattern this stretch: **check a feature against the richest real
data you have before trusting it** — thin/synthetic data hid both the wkdelta scope problem and the
Trends summary bug.

---

## Operator perspective

*(first person — Derek)*

I keep proving to myself that the only real test is looking at the thing with real data. Shape built
clean and the AI had it "verified," but two things only showed up because I poked it: the window
selector charts moved while the summary numbers sat still, and tonight's whole raid was just...
missing. Neither was in any green checkmark.

The call I delegated well this time was the review — "ask NASA bro and MIT uncle to take a look." That
wasn't ceremony; it caught a real architecture error (wkdelta scope) and made the AI re-check its own
claim against the code instead of asserting from memory. And "make sure the plan holds now the data is
in" was the right instinct — the data check flipped the design before we wasted a build on the wrong
scope. That's the pattern I want: decide against the data, not against the vibe.

What felt off: the window kept dying on me and I kept having to ask for relaunches, and there was a
stretch where the AI was confidently telling me the corpus was too thin when it just wasn't looking at
the career view. When I said "parse more logs should help, no?" the honest answer turned out to be
"no, but you're missing tonight's raid entirely" — which is the kind of thing I'd rather hear before I
ask. The fix landed fast once I pointed at it, but I'm the one who had to point.

The good vibe: when it works, it works. Crown of the Cosmos — 2 kills against 52 wipes, and the
contrast actually told me something (way fewer deaths on the kills, made it to the end). That's the
"oh" moment the whole thing is for, finally on my own data.

---

## How we worked together (human <-> AI)

### What worked well

- **Operator-requested adversarial review caught a real error pre-build.** "ask NASA bro and MIT
  uncle" produced two grounded critiques; the V&V pass *corrected the AI's own false claim* that
  career fan-in lived in the per-night artifact path (it doesn't — `CareerProjection` does no I/O).
  That correction reshaped the build before any code was written.
- **"Make sure the plan holds now the data is in" -> a 5-minute data check that reversed the design.**
  Running `/api/career` and finding 48/119 mixed-outcome careers turned wkdelta from "dead on every
  night" into the strongest view. Validating against real data beat reasoning about it.
- **The commit was held for the operator's eyes.** The AI verified the artifact layer and then
  explicitly waited on "looks good" for the pixel layer rather than claiming done — last retro's
  "verified needs a layer" lesson, actually applied.
- **Honesty corrections were designed in, not bolted on.** The brief was rewritten (hero = "long-
  exposure of a pull" not "signature of a fight"; no "averaged" at N=1; first-class one-sided frame)
  *before* `ShapeTab.jsx` existed, so the UI shipped honest the first time.
- **Headless mode dodged the window-thrash for data checks** — running `--headless` for the career
  and density inspections avoided spawning (and losing) a WebView2 window each time.

### What didn't

- **The AI reasoned about data richness from a single lens and got it wrong — twice.** First it
  concluded the corpus was too thin from the *per-night* pull counts ("max 2 pulls/boss"); then it
  claimed career fan-in was reachable in the per-night path. Both were corrected by the data
  check / the reviewer, not by the AI. Same root error: one scope, asserted as the whole picture.
- **The "missing fights" blind spot.** The backfill re-touched the 74 *already-parsed* nights but
  never checked for *new, unparsed* logs — so tonight's 628 MB raid sat invisible until the operator
  noticed. "Refresh everything" didn't include discovering the new thing.
- **Window-close thrash continued.** The desktop window needed repeated relaunches across the
  session (clean exits — almost certainly the operator closing windows that looked wrong), costing
  back-and-forth. Never fully pinned, worked around with headless.
- **A bug shipped earlier stayed hidden behind thin data.** The Trends "Across the window" summary
  showed the latest pull's value (constant across window sizes) since the original Trends work
  (`4886d05`); the thin synthetic corpus hid it, and only the 26-pull Midnight Falls career made it
  visible. The data that fixed Shape also exposed an older bug.
- **PowerShell papercuts again** — `curl | python` JSON parses failed whenever the host was down,
  burning a couple of calls before checking host health.

### Patterns to repeat

- Validate a plan against real data (a quick read-only query) before building — especially any claim
  about whether data is rich enough.
- Spin up an adversarial/persona review before building a new surface; require it to cite code, not
  memory.
- Hold the commit for the operator's pixel-eyeball on anything visual.
- Version-suffix the cache key on any artifact schema change so a re-parse backfills it.

### Patterns to change

- When judging whether data supports a feature, check **both** scopes (per-night AND per-career)
  before concluding — never reason from one lens.
- A "refresh/backfill" must include **discovery of new inputs**, not just re-processing known ones.
  Add a new-logs check.
- Lead with the thing the operator most needs to hear (your raid isn't parsed) before answering the
  narrower question they asked (will parsing more help).

---

## Lessons learned

1. **Thin data hides bugs that real data exposes.** Both the wkdelta scope problem and the Trends
   summary-number bug were invisible on the thin/synthetic corpus and obvious on a 26-pull boss.
   Verify new features against the richest real data you have, as early as possible.
2. **The same metric is "starved" or "rich" depending on aggregation scope.** Per-night, kill-vs-wipe
   had nothing to contrast; per-career, 48 bosses had plenty. Always check the scope the feature
   actually operates at before declaring it data-blocked.
3. **A backfill that only re-processes known items silently misses new arrivals.** "Update
   everything" must discover, not just iterate.
4. **Adversarial review before building is cheaper than discovering after.** A persona review caught an
   architecture error and a factual misstatement before they cost a build.
5. **Only the human verifies pixels.** The AI can prove the artifact layer; "looks good" is a check
   the AI structurally cannot perform. Build the workflow around that division.

---

## Next moves

- **Push** — local `master` is 4 commits ahead of origin (`git push origin master`).
- **Pay the test debt** (now three deep): Shape density invariants + career wkdelta endpoint; an
  `/ollama/api/tags`-through-the-proxy smoke (the IPv4 bug); a trends v2 artifact-shape assertion.
  Tests live in `D:\work\leopard\src\leopard-host.Tests`.
- **"New logs detected — parse them" nudge** on the Setup tab — prevents the missing-fights class of
  confusion. Offered, not built.
- **Start the ADR log** — `docs/adr/` still doesn't exist. Strongest candidates this stretch:
  career-scoped wkdelta (chose X over Y because the data demanded it) and the artifact
  cache-versioning pattern.
- **Shape v2** — the cross-attempt density overlay (where "signature" is earned), flagged in
  `D:\work\leopard\docs\shape-design-brief.md` section 8.
- **The authoring surface / the Rack** — the next big one after Shape (`docs/feature-order.md`).

---

## Acceptance gates met

- [x] Shape v1 shipped — per-pull density heatmap + career-scoped kill-vs-wipe (`ae5ebff`)
- [x] Brief reviewed (V&V + statistical-honesty) and corrected before build (`3aef269`)
- [x] Plan validated against real data — career-scope decision from the `/api/career` check
- [x] Verified both layers — artifact (invariants on real data) + operator pixel ("looks good")
- [x] Corpus backfilled — 74/74 Shape artifacts, 70/74 with movement heatmaps
- [x] Tonight's raid parsed — 6 fights recovered (`WoWCombatLog-060626_175859.txt`)
- [x] Trends "Across the window" summary fixed to the window mean (`b3bf884`)
- [x] Night picker moved above the tabs — global chrome, not per-tab (`ddc8afe`)
- [x] Tests 5/5
- [ ] Automated tests for Shape / proxy / trends-shape — deferred, debt accumulating
- [ ] Pushed to origin — deferred (4 ahead)
- [ ] ADR log started — deferred (proposed twice)
