# Property Inventory — Leopard

**Version:** 1  
**Status:** Living document — add properties as surfaces ship; bump `@vN` suffix when semantic meaning changes.

The inventory is the load-bearing metadata for the query builder (lens composer). Every property gets a **stable, dotted, versioned ID** as its primary key. Display labels are cosmetic; IDs are the contract. A saved lens references properties by ID — display names can change, IDs must not. The `@vN` suffix bumps only when a property's **semantic meaning** changes (e.g. `bossEndPctHp` starts including phase-transition frames → becomes `@v2`), making stale meanings detectable.

---

## Namespaces

| Prefix | Scope | Source artifact |
|---|---|---|
| `raid.night.*` | one raid session (the selected night) | Box score |
| `raid.encounter.*` | one boss encounter within a night | Box score |
| `raid.pull.*` | one pull within an encounter | Box score, Shape |
| `career.*` | all-time boss career (all nights fanned in) | Career summary, Career-input |
| `roster.*` | all-time roster row per boss | Roster (`/api/career`) |
| `trend.window.*` | per-encounter rule-row window (last N pulls) | Trends artifact |
| `trend.coherence.*` | per-encounter coordination metrics in a window | Trends artifact |
| `shape.encounter.*` | per-boss shape metadata (this night) | Shape artifact |
| `shape.pull.*` | per-pull scalars from Shape | Shape artifact |
| `shape.density.*` | 32×16 heatmap grid | Shape artifact — **viz-only** |
| `trace.*` | parser pipeline substrate metadata | Trace artifact — **engine meta, not raid** |

---

## Confidence propagation rule

- **Exact facts** (`exact=true`): `confidence = 1.0` — computed directly from log data with no inference.
- **Means and counts of exact values** (e.g. `career.avgDeaths`): `confidence = 1.0` — exact arithmetic over exact inputs.
- **Direction / trend labels** (categorical inference from a noisy series): `confidence = min(pullCount / 10.0, 0.95)` — grows with data volume; a 6-pull career scores 0.60, a 20-pull career 0.95.
- **Composite derivations**: propagate the minimum confidence of the inputs.

---

## Box Score artifact (`BoxScore.cs`)

Produced at parse time. Format: markdown blob. Not field-addressable until a structured emit is added (deferred C# work — see notes).

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `raid.night.zone@v1` | Raid zone | night | string | exact | — | 1.0 | context | no | repo (`BoxScore.cs:22`) |
| `raid.night.date@v1` | Raid date | night | string (ISO date) | exact | — | 1.0 | context | no | repo (`BoxScore.cs:23`) |
| `raid.night.difficulty@v1` | Difficulty | night | string | exact | — | 1.0 | context | no | repo (`BoxScore.cs:24`) |
| `raid.night.playerCount@v1` | Player count | night | int | exact | — | 1.0 | context | no | repo (`BoxScore.cs:25`) |
| `raid.night.bossesKilled@v1` | Bosses killed | night | int | exact | — | 1.0 | context | no | repo (`BoxScore.cs:32`) |
| `raid.night.bossesInProgress@v1` | Bosses in progress | night | int | exact | — | 1.0 | context | no | repo (`BoxScore.cs:32`) |
| `raid.encounter.name@v1` | Boss name | encounter | string | exact | — | 1.0 | context | no | repo (`BoxScore.cs:39,49`) |
| `raid.pull.deaths@v1` | Deaths in pull | pull | int | exact | — | 1.0 | context | no | repo (`BoxScore.cs:40,48`) |
| `raid.pull.durationMs@v1` | Pull duration (ms) | pull | long | exact | — | 1.0 | context | no | repo (`BoxScore.cs:40,65`) |
| `raid.pull.bossEndPctHp@v1` | Boss HP at pull end (%) | pull | float (0–100, 0=kill) | exact | — | 1.0 | context | no | repo (`BoxScore.cs:52–57`) |
| `raid.encounter.deathTrend@v1` | Death trend label | encounter | string | derived | `raid.pull.deaths@v1` | `min(pullCount/10,0.95)` | context | no | repo (`BoxScore.cs:81–90`) |
| `raid.encounter.bestProgressPct@v1` | Best boss HP reached (%) | encounter | float | derived | `raid.pull.bossEndPctHp@v1` | 1.0\* | context | no | repo (`BoxScore.cs:52–59`) |
| `raid.encounter.fastWipePulls@v1` | Fast wipe pull numbers (≤30 s) | encounter | int[] | exact | — | 1.0 | context | no | repo (`BoxScore.cs:61–63`) |
| `raid.encounter.longestPulls@v1` | 3 longest pull summaries | encounter | string | exact | — | 1.0 | context | no | repo (`BoxScore.cs:65–66`) |

\* `bestProgressPct` is a `min()` over exact figures — exact arithmetic, `confidence=1.0`.

> **Structured emit deferred.** Box score fields above are today embedded in a pre-rendered markdown blob (`BoxScore.Build()` returns `StringBuilder.ToString()`). Field-level selection requires a structured emit alongside the markdown blob — C# work depending on the Tempo sibling repo, deferred to a Windows session.

---

## Career Summary artifact (`CareerSummary.cs`)

Produced at `/api/career-summary?careerId=…`. Format: markdown blob. Fan-in across all parsed nights via `CareerProjection`.

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `career.name@v1` | Boss name | career | string | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:33`) |
| `career.difficulty@v1` | Difficulty | career | string | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:33`) |
| `career.attempts@v1` | Total pulls | career | int | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:27`) |
| `career.nights@v1` | Raid nights pulled | career | int | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:29`) |
| `career.kills@v1` | Kill count | career | int | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:28`) |
| `career.killed@v1` | Whether killed | career | bool | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:30`) |
| `career.firstPulled@v1` | Date of first pull | career | string (ISO date) | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:43`) |
| `career.lastPulled@v1` | Date of most recent pull | career | string (ISO date) | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:43`) |
| `career.firstKillAttempt@v1` | Attempt number of first kill | career | int | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:48–49`) |
| `career.bestProgressPct@v1` | Lowest boss HP reached (%) | career | float | derived | `raid.pull.bossEndPctHp@v1` | 1.0 | context | no | repo (`CareerSummary.cs:53`) |
| `career.direction@v1` | Trend direction label | career | string (improving/slipping/steady) | derived | `raid.pull.bossEndPctHp@v1` | `min(attempts/10,0.95)` | context | no | repo (`CareerSummary.cs:78–89`) |
| `career.avgDeaths@v1` | Average deaths per pull | career | float | derived | `raid.pull.deaths@v1` | 1.0 | context | no | repo (`CareerSummary.cs:59–61`) |
| `career.peakDeaths@v1` | Maximum deaths in any pull | career | int | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:61`) |
| `career.progressArc@v1` | Boss HP per pull, oldest→newest | career | int[] | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:65–66`) |

> **Structured emit deferred.** Same situation as box score — markdown blob today; structured emit is the C# prerequisite for field-level selection.

---

## Career-input artifact (`.career.json` — `EncountersProjection.cs` in Tempo)

Produced at parse time and fanned into roster, career-summary, wkdelta, and shape-wkdelta. **The cleanest structured substrate** — no markdown rendering, directly queryable. Fields confirmed from consumption in this repo.

### Encounter-level fields (RaidViewEncounter)

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `career.enc.id@v1` | Encounter ID | encounter | string | exact | — | 1.0 | context | no | repo (`Program.cs:198,210,253`) |
| `career.enc.name@v1` | Boss name | encounter | string | exact | — | 1.0 | context | no | repo (many sites) |
| `career.enc.difficulty@v1` | Difficulty | encounter | string | exact | — | 1.0 | context | no | repo |
| `career.enc.careerId@v1` | Stable all-time boss identity | encounter | string | exact | — | 1.0 | context | no | repo (`Program.cs:128,204`) |
| `career.enc.kills@v1` | Kill count for this encounter | encounter | int | exact | — | 1.0 | context | no | repo (`TrendsArtifact.cs:58`) |

### Pull-level fields (RaidViewPull)

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `raid.pull.id@v1` | Pull ID | pull | string | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:38`) |
| `raid.pull.n@v1` | Pull number (1-indexed) | pull | int | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:54`) |
| `raid.pull.outcome@v1` | Outcome string ("Kill"/wipe) | pull | string | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:55`) |
| `raid.pull.bossEndPctHp@v1` | Boss HP at pull end (%) | pull | float | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:76,ShapeArtifact.cs:56`) |
| `raid.pull.durationMs@v1` | Pull duration (ms) | pull | long | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:53,ShapeArtifact.cs:57`) |
| `raid.pull.deaths@v1` | Deaths in pull | pull | int | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:59,ShapeArtifact.cs:58`) |
| `raid.pull.startedAt@v1` | Pull start timestamp (ISO) | pull | string | exact | — | 1.0 | context | no | repo (`CareerSummary.cs:42`) |

> **Disk-to-confirm:** The Tempo sibling repo's `RaidViewPull` and `RaidViewEncounter` types may carry additional fields beyond what this repo consumes. Enumerate against a real `.career.json` in `%LOCALAPPDATA%\Leopard\cache` on the Windows machine (pick the longest career available, e.g. a long Crown of the Cosmos arc) to confirm the complete field set.

---

## Roster artifact (`CareerRoster.cs` → `/api/career`)

Fan-in across all parsed nights. Format: structured JSON (`{ bosses: RosterRow[] }`). **Directly field-addressable today.**

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `roster.careerId@v1` | Stable all-time boss identity | roster | string | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:81`) |
| `roster.name@v1` | Boss name | roster | string | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:82`) |
| `roster.difficulty@v1` | Difficulty | roster | string | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:83`) |
| `roster.attempts@v1` | Total pulls all-time | roster | int | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:51,84`) |
| `roster.kills@v1` | Total kill count | roster | int | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:44,85`) |
| `roster.killed@v1` | Whether ever killed | roster | bool | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:45,86`) |
| `roster.bestPct@v1` | Best boss HP reached (%) | roster | float? | derived | `raid.pull.bossEndPctHp@v1` | 1.0 | context | no | repo (`CareerRoster.cs:54,87`) |
| `roster.totalTimeMs@v1` | Total time on boss all-time (ms) | roster | long | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:55,88`) |
| `roster.firstSeen@v1` | Date of first pull | roster | string (ISO) | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:56,89`) |
| `roster.lastSeen@v1` | Date of most recent pull | roster | string (ISO) | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:57,90`) |
| `roster.direction@v1` | Trend direction label | roster | string (improving/slipping/steady/new) | derived | `raid.pull.bossEndPctHp@v1` | `min(attempts/10,0.95)` | context | no | repo (`CareerRoster.cs:71–78`) |
| `roster.arc@v1` | Boss HP per pull, all-time | roster | float[] | exact | — | 1.0 | context | no | repo (`CareerRoster.cs:59`) |

---

## Trends artifact (`.trends.v2.json` — `TrendsArtifact.cs`)

Produced at parse time. Format: structured JSON. Per-encounter cards; each card has four window sizes (4/6/8/10 pulls) of rule rows and coherence series. Default window: 6.

### Window rule rows (`trend.window.*`)

Rule rows are produced by `TrendsProjection` (Tempo sibling repo). Labels from the projection; confirmed against `trendSummaryText` consumption (`AskPanel.jsx` → now `context.js:serializeTrend`).

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `trend.window.kills@v1` | Kills in window | window | int | exact | — | 1.0 | context | no | repo (rule row `label="Kills"`) |
| `trend.window.killsDelta@v1` | Kill count change vs prior window | window | int | derived | `trend.window.kills@v1` | 1.0 | context | no | repo (rule row `delta`) |
| `trend.window.avgDeaths@v1` | Avg deaths in window | window | float | derived | `raid.pull.deaths@v1` | 1.0 | context | no | repo (rule row `label="Avg deaths"`) |
| `trend.window.avgDeathsDelta@v1` | Deaths change vs prior window | window | float | derived | `trend.window.avgDeaths@v1` | 1.0 | context | no | repo (rule row `delta`) |
| `trend.window.bestProgressPct@v1` | Best boss HP in window (%) | window | float | derived | `raid.pull.bossEndPctHp@v1` | 1.0 | context | no | repo (rule row `label="Best progress %"`) |
| `trend.window.bestProgressDelta@v1` | Progress change vs prior window | window | float | derived | `trend.window.bestProgressPct@v1` | 1.0 | context | no | repo (rule row `delta`) |
| `trend.window.avgDurationMs@v1` | Avg pull duration in window (ms) | window | long | derived | `raid.pull.durationMs@v1` | 1.0 | context | no | repo (rule row `label="Avg duration"`) |
| `trend.window.avgDurationDelta@v1` | Duration change vs prior window | window | string | derived | `trend.window.avgDurationMs@v1` | 1.0 | context | no | repo (rule row `delta`) |
| `trend.window.dir@v1` | Direction label (better/worse/flat) | window | string | derived | rule row value + prior | `min(windowSize/10,0.95)` | context | no | repo (rule row `dir`) |

> **Disk-to-confirm:** The full set of rule row labels produced by `TrendsProjection` (Tempo sibling). Confirm against a real `.trends.v2.json` artifact.

### Coherence series (`trend.coherence.*`)

Requires advanced combat logging (replay/movement frames). `null-if-no-replay = yes`.

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `trend.coherence.followershipMean@v1` | Followership mean (how together the raid moves) | window | float | derived | movement positions | ~0.8 (movement sampling noise) | context | **yes** | repo (`context.js:serializeTrend`, formerly `AskPanel.jsx:161`) |
| `trend.coherence.entropyMean@v1` | Entropy mean (how spread their movement is) | window | float | derived | movement positions | ~0.8 | context | **yes** | repo (`context.js:serializeTrend`) |
| `trend.coherence.peakSpeed@v1` | Peak movement speed (yd/s) | window | float | derived | movement positions | ~0.8 | context | **yes** | repo (`context.js:serializeTrend`) |

> **Disk-to-confirm:** Full coherence point schema (fields beyond fol/ent/spd) and exact sampling semantics. Confirm against a `.trends.v2.json` artifact from a night with advanced logging enabled.

---

## Shape artifact (`.shape.v1.json` — `ShapeArtifact.cs`)

Produced at parse time. Format: structured JSON. Per-encounter cards, each with per-pull entries. Density grid is **viz-only** (not context-groundable).

### Per-encounter scalars (`shape.encounter.*`)

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `shape.encounter.careerId@v1` | All-time boss identity (ties to career/wkdelta) | encounter | string | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:67`) |
| `shape.encounter.name@v1` | Boss name | encounter | string | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:68`) |
| `shape.encounter.difficulty@v1` | Difficulty | encounter | string | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:69`) |
| `shape.encounter.kills@v1` | Kill count this night | encounter | int | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:70`) |
| `shape.encounter.pullCount@v1` | Total pulls this night | encounter | int | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:71`) |

### Per-pull scalars (`shape.pull.*`)

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `shape.pull.hasMovement@v1` | Whether pull has movement frames | pull | bool | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:59`) |
| `shape.pull.outcome@v1` | Outcome ("Kill"/wipe) | pull | string | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:55`) — same semantic as `raid.pull.outcome@v1` |
| `shape.pull.bossEndPctHp@v1` | Boss HP at pull end (%) | pull | float | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:56`) — same semantic as `raid.pull.bossEndPctHp@v1` |
| `shape.pull.durationMs@v1` | Pull duration (ms) | pull | long | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:57`) — same semantic as `raid.pull.durationMs@v1` |
| `shape.pull.deaths@v1` | Deaths in pull | pull | int | exact | — | 1.0 | context | no | repo (`ShapeArtifact.cs:58`) — same semantic as `raid.pull.deaths@v1` |

> **Cross-artifact note:** `shape.pull.{outcome,bossEndPctHp,durationMs,deaths}@v1` carry the same semantic as `raid.pull.*@v1`. A future composer should deduplicate these — one property, multiple source artifacts.

### Density grid (`shape.density.*`) — VIZ-ONLY

| ID | Label | Scope | Type | exact/derived | derivedFrom | confidence | groundable | null-if-no-replay | confirmed |
|---|---|---|---|---|---|---|---|---|---|
| `shape.density.gridW@v1` | Grid width (cols) | pull | int (32) | exact | — | 1.0 | **viz-only** | **yes** | repo (`ShapeArtifact.cs:24,45`) |
| `shape.density.gridH@v1` | Grid height (rows) | pull | int (16) | exact | — | 1.0 | **viz-only** | **yes** | repo (`ShapeArtifact.cs:25,46`) |
| `shape.density.cells@v1` | 512-cell normalized density grid (0..1) | pull | float[512] | derived | movement positions | ~0.8 | **viz-only** | **yes** | repo (`ShapeArtifact.cs:47`) |
| `shape.density.totalSamples@v1` | Raw frame/sample count | pull | int | exact | — | 1.0 | **viz-only** | **yes** | repo (`ShapeArtifact.cs:48`) |
| `shape.density.arenaW@v1` | Arena width (yards) | pull | float | exact | — | 1.0 | context | **yes** | repo (`ShapeArtifact.cs:50`) |
| `shape.density.arenaH@v1` | Arena height (yards) | pull | float | exact | — | 1.0 | context | **yes** | repo (`ShapeArtifact.cs:51`) |

---

## Trace artifact (`.trace.json` — `PipelineTrace.cs`)

Engine/pipeline metadata for the Pipeline Explorer. Not raid performance data — **meta only**.

| ID | Label | Scope | Type | groundable | confirmed |
|---|---|---|---|---|---|
| `trace.*` | Pipeline stage substrate counts, samples, trim collapse | night | structured JSON | **meta only — not raid context** | repo (`PipelineTrace.cs`) |

---

## Summary: context-groundable vs viz-only

| Property family | Context-groundable | Viz-only |
|---|---|---|
| Box score fields | ✓ (blob today; field-level after C# structured emit) | — |
| Career summary fields | ✓ (blob today) | — |
| Career-input pull/encounter fields | ✓ | — |
| Roster row fields | ✓ | — |
| Trend window rule rows + deltas | ✓ | — |
| Trend coherence signals | ✓ (when movement data present) | — |
| Shape encounter + pull scalars | ✓ | — |
| Shape density grid (512 cells) | — | ✓ (too large; visual substrate) |
| Arena dimensions | ✓ | — |
| Trace (pipeline meta) | — | ✓ (not raid performance) |

---

## Windows-side verification checklist

Run these after opening a real cache file on the Windows machine:

- [ ] Open a `.career.json` for a long career (Crown of the Cosmos recommended). Confirm all `raid.pull.*` fields exist with stated types. Note any additional fields (`disk-to-confirm` → promote to `repo-confirmed` with source file/line).
- [ ] Open a `.trends.v2.json`. Confirm the complete rule row label set. Note any rule rows not listed above.
- [ ] Open a `.trends.v2.json` from a night with advanced logging. Confirm coherence point schema (all keys beyond fol/ent/spd).
- [ ] Open a `.shape.v1.json`. Confirm density cell count (should be exactly `gridW × gridH = 512`). Note pull fields beyond those listed.
