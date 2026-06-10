# Live — between-pull insight + expert feedback (design brief)

**What this is:** while actively raiding (`/combatlog` on), Leopard detects that a pull just
ended, pre-generates one tight grounded insight on the second B70, and has it ready before the
operator looks — first in a Leopard tab, ultimately painted over the game by discoverlay. The
operator grades it in two taps. Those grades, joined to the full replayable context, become the
eval corpus for the critic-loop work.

**What this is NOT:** coaching tips for raiders. The consumer of v1 is the operator — the one
person who is simultaneously the platform's builder, its domain expert, and the source of ground
truth. The between-pull window is the highest-fidelity labeling moment the product will ever get:
the expert just *lived* the pull, and his memory of it decays in minutes. v1 is an expert
labeling station that happens to look like an insight card.

*Predecessor: a cloud session implemented an earlier draft of this (regex log tail, hardcoded
Ollama) that never escaped its container. This brief supersedes that draft — see "What changed
from the first draft" at the bottom.*

---

## 1. The three-repo division

The loop spans three codebases, split along seams each one already drew:

```
WoW writes WoWCombatLog*.txt (live)
        │
        ▼
┌─ Tempo owns log → facts ───────────────────────────────────────────────┐
│  Tempo.Core.Ingest.FileSystemLogMonitor   (interrupt-driven tail:      │
│    flush-aware, rotation-safe, overflow-recovering, encounter-resume)  │
│  Tempo.Core.Ingest.CombatLogParser        (typed Events: EncounterStart│
│    /End w/ success + fightDurationMs, UnitDied, advanced-logging boss  │
│    HP on damage events)                                                │
│  — consumed IN-PROCESS by Leopard (already a ProjectReference).        │
│    Zero Tempo changes.  ADR-018 calls this lane "the post-wipe         │
│    directive path"; we are its first consumer.                         │
└────────────────────────────────────────────────────────────────────────┘
        │ typed events
        ▼
┌─ Leopard owns facts → grounded insight → record ───────────────────────┐
│  LiveSession (new, src/leopard-host/LiveSession.cs)                    │
│   · accumulates TONIGHT's pulls in memory (the trajectory)             │
│   · joins the cached all-time career row (CareerSummary over the same  │
│     fan-in /api/career uses — a lookup, no new math)                   │
│   · builds the evidence text → POSTs to the configured OpenAI-style    │
│     endpoint (default llama-server on the 2nd B70, :8080)              │
│   · appends every lifecycle event to live-insight.jsonl (replayable)   │
│  Endpoints: /api/live/status · /api/live/insight · /api/live/feedback  │
│  LiveTab: the review desk (card + evidence disclosure + 2 taps + note) │
└────────────────────────────────────────────────────────────────────────┘
        │ live-insight.jsonl (file bridge)
        ▼
┌─ discoverlay owns delivery (separate repo, separate build) ────────────┐
│  composer: tailer + projector + build_insight_panel; combat-aware      │
│  (panel hidden during a pull, appears on ENCOUNTER_END);               │
│  hud.exe paints it over the game — no alt-tab on the single monitor.   │
│  Feedback taps as global hotkeys later (Ctrl+U precedent).             │
└────────────────────────────────────────────────────────────────────────┘
```

The file bridge makes "who is the brain" cheap to revise: if Tempo.Host's directive path matures
into the live brain, it takes over writing `live-insight.jsonl` and nothing downstream changes.

## 2. Evidence — the load-bearing decision

A pull's five scalars (boss, outcome, duration, deaths, boss %) alone produce horoscope text, and
grading horoscope text poisons the feedback corpus: every 👎 measures input starvation, not model
quality. The evidence block has three layers, all real, all already computable:

1. **The pull** (live, from Tempo's typed events): boss, difficulty, kill/wipe,
   duration (`fightDurationMs` is on the ENCOUNTER_END line — no START pairing), player deaths
   (`UnitDied` filtered to `Player-` GUIDs), and boss-at-end % (advanced-logging
   `currentHp/maxHp` tracked per creature during the pull; the largest-`maxHp` creature is the
   boss — labeled approximate, the canonical number comes from the real parse later).
2. **Tonight** (in-memory): every prior pull on this boss this session — best %, death trend,
   pull count. The within-night trajectory no cached artifact carries.
3. **All-time** (cached): the career summary (`CareerSummary.Build`) matched by boss
   name + difficulty against the fanned career inputs — the same exact-figures text the Ask
   career zoom reads. Absent for a first-ever boss; the evidence says so instead of faking it.

Display==send holds here exactly as everywhere else: the card shows the evidence block the model
read, byte-identical, behind a disclosure.

## 3. The prompt

```
System: You are Leopard, a reflection engine for a World of Warcraft raid team. A pull just
        ended; this is read between pulls, so be brief: 2-3 sentences, then ONE open question
        worth discussing. Restate only the figures provided — never invent numbers. Momentum
        over perfection: name improvement as readily as problems. If the evidence shows a
        trajectory, speak to the trajectory, not just the last pull.

User:   <the three-layer evidence block> 
        Reflect briefly and end with one question worth raising with the group.
```

Pre-generated, never awaited: inference starts the moment ENCOUNTER_END parses. A new pull
cancels an in-flight generation (CancellationTokenSource) — the freshest pull always wins.

## 4. The record — one artifact, two consumers

Every lifecycle event appends one JSON line to `%LOCALAPPDATA%\Leopard\live-insight.jsonl`:

```jsonc
{ "kind":"pull",     "v":1, "ts":"...", "pullId":"...", "boss":"...", "difficulty":"Heroic",
  "kill":false, "durationMs":134000, "playerDeaths":3, "bossEndPct":34.2, "tonightIndex":6 }
{ "kind":"insight",  "v":1, "ts":"...", "insightId":"...", "pullId":"...", "state":"pending" }
{ "kind":"insight",  "v":1, "ts":"...", "insightId":"...", "pullId":"...", "state":"ready",
  "evidence":"<exact text>", "system":"<exact text>", "model":"...", "url":"...",
  "params":{"temperature":0.4,"max_tokens":220}, "text":"...", "latencyMs":4100 }
{ "kind":"feedback", "v":1, "ts":"...", "insightId":"...", "useful":true, "grounded":true,
  "comment":"missed that the deaths were all the same mechanic" }
```

Consumers: (a) the discoverlay composer tails it for the overlay; (b) the critic-loop regression
harness reads it as the eval corpus — every judged insight is fully replayable (evidence + prompt
+ params + output). Feedback joins by `insightId`. discoverlay's `--replay` mode gets "re-watch a
raid night's insights" for free.

The two taps are named axes, not vibes: **useful** (worth reading between pulls?) and
**grounded** (did it stick to the evidence?). Orthogonal failure modes, recorded separately.

## 5. Config

`LeopardConfig` grows two optional fields (existing configs keep working):

- `liveInferenceUrl` — default `http://127.0.0.1:8080/v1/chat/completions` (llama-server on the
  2nd B70 per the runbook; vllama's :8090 facade also speaks this). NOT the Ollama-only
  `/api/chat` of the first draft.
- `liveModel` — model name passed through (llama-server ignores it; vllama routes on it).

The watcher follows the configured `logDir`, re-targets the newest `WoWCombatLog*.txt`, and
restarts when config changes.

## 6. Verification ladder

1. `GET /api/live/status` → `active:false` with no log activity (endpoint wiring).
2. Append synthetic lines (real CLF shapes: ENCOUNTER_START, SPELL_DAMAGE w/ advanced fields,
   UNIT_DIED, ENCOUNTER_END w/ fightTime) to a test file in the log dir → status flips, pull
   facts extracted correctly (tail + parser integration, no WoW needed).
3. With llama-server up: insight goes `pending` → `ready`; with it down: `error` state, card
   says so honestly, next pull retries (failure path is a feature — raid night must not care).
4. LiveTab: card renders, taps + comment POST, line lands in the jsonl, next pull re-arms.
5. Real raid night: `/combatlog`, pull, alt-tab — card already there. (Overlay delivery makes
   even the alt-tab unnecessary; that's the discoverlay half.)

## 7. Deliberately out of v1

- **Overlay delivery + hotkey taps** — discoverlay-side build, separate brief/commits.
- **Mid-pull state or callouts** — Tempo.Host's directive path territory; v1 only speaks
  between pulls.
- **Insight history UI** — the jsonl keeps everything; the card shows the latest. A history
  view can read the file later.
- **Live Tempo full-parse** — the canonical artifacts still come from PARSE after the night;
  live facts are staging, reconciled by the join keys in the record.

## What changed from the first draft (the cloud build)

| First draft | This brief | Why |
|---|---|---|
| 512KB regex tail in Leopard | `Tempo.Core.Ingest` typed events | The tail/parse problem was already solved, tested, in the referenced assembly |
| 5 scalars as evidence | 3-layer evidence (pull + tonight + career) | Starved evidence makes feedback unusable as an eval signal |
| Hardcoded Ollama `:11434/api/chat` | Config `liveInferenceUrl`, OpenAI-style, default `:8080` | That's what actually runs on the rig |
| `{thumbs, vote}` feedback | Named axes + full replayable record | The corpus is the point, not the taps |
| Card shows answer only | Evidence disclosure, display==send | Grounded grading requires seeing what the model read |
