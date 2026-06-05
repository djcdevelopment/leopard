# The Roster — multi-run / multi-boss career view

**Status:** Locked 2026-06-05. The validated next surface after Trends + the Pipeline
Explorer. A *fan-in across runs* — the next worked example of the teach-to-author chain.

## What it is
Every boss this tier as a row — a glanceable wall of mini-careers. For each boss: all-time
kills, best % reached, total attempts, total time, and a direction (improving / stalling),
with a full-career sparkline. Drill into any row for the boss's whole arc.

## The locked decisions (2026-06-05)
- **All-time career**, not a rolling "last X" window. The full arc since the boss was first
  pulled. (Rolling windows can come later; all-time is v1.)
- **Heroic and Mythic are separate careers.** Tempo's `careerId = slug(name)__slug(difficulty)`
  already splits them — they're genuinely different fights. Zero extra work.
- **Roster presentation** — the whole-raid overview, drill into one. (Not a one-boss picker.)

## Grounded in existing engine math
`CareerProjection.TryResolveCareerEncounter` (Tempo.Projections) already merges a boss across
sessions: it groups by `careerId`, orders pulls chronologically, and re-numbers them 1..N.
`TrendsProjection` already runs off it. So the cross-night math is **done** — multi-run is an
*input-aggregation* problem, not a new-engine problem.

## Honest design note
"All-time" does NOT fit `TrendsProjection`'s recent-window delta model (last-N-vs-prior-N goes
flat once the window spans the whole career). So the Roster is a *distinct projection*: career
**aggregates** (totals, best-ever) + the **full-career arc** as the sparkline, with "direction"
= early-third vs late-third. `CareerProjection` supplies the merged timeline; the Roster
aggregates from it directly.

## Architecture (same seam, no engine changes)
- **Per-night career-input artifact** cached at parse time: pull metadata (outcome, deaths,
  duration, boss-end-%HP, timestamps, `careerId`) + the per-pull coherence floats we already
  compute. Small — no event streams, no replays.
- **`/api/career`**: loads every parsed night's artifact, concatenates encounters, groups by
  `careerId`, runs `CareerProjection`, aggregates the roster. No re-parsing.
- **Roster tab** (or a mode on Trends): the rows + drill-in.

## Scope
- **v1:** read-only roster, all-time, separate careers, all parsed nights.
- **Not v1:** rolling "last X" windows, per-player careers, share/export, cross-tier history.
