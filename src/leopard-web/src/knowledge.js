// Knowledge-object registry for the Explorer (the knowledge library).
// Pure data + helpers, no React — the lens.js pattern. Every leaf in the Explorer's
// PROJECT EXPLORER tree is one entry here. status:'live' entries have a working fetch
// path today (the four exposed RaidUI math ports); status:'ghost' entries carry the
// full taxonomy so phase 2 only flips status + adds an api key — the tree never changes shape.
//
// IDs follow the versioned convention from lens.js's CAREER_PALETTE (name@v1) — stable,
// digest-friendly, matching docs/property-inventory.md.

export const CATEGORY_ORDER = ['MOMENTS', 'EVENTS', 'REPLAY', 'MOVEMENT', 'MECHANICS', 'PROGRESSION']

// Starred QUESTIONS leaves — selecting one seeds the Investigation input.
export const EXPLORER_SEEDS = [
  'Why are people dying?',
  'Is the raid moving together?',
  'Are the healers keeping up?',
  'Which pull was our best, and what made it different?',
  'How far are we getting?',
]

// api: which api.js fetcher feeds this object ('signals'|'players'|'affinity'|'diff'|null).
// stage/truth/stream/confidence/promptFit/vizFit: the Properties-panel metadata rows.
// sliceOptions: the dropdown values the Properties panel offers; sliceDefaults: initial pick.
// Options marked real in contract.js actually reshape the payload; the rest are recorded
// in the XML as annotations (the compiler doesn't branch on them yet).
export const KNOWLEDGE_OBJECTS = [
  // ── MOMENTS ──────────────────────────────────────────────────────────────
  {
    id: 'moment.view@v1', label: 'Moment (view)', category: 'MOMENTS', status: 'ghost', api: null,
    stage: 'Derived · 06', truth: 'DERIVED', confidence: 'med', stream: 'widened',
    promptFit: 'high', vizFit: 'high',
    builtFrom: ['Raw event'], feeds: ['Evidence item'],
    description: 'Significant combat moments (death / phase / save / feast / swap) detected over the widened categorical stream. Lights up when Tempo moments reach a host endpoint.',
  },
  {
    id: 'classify.wipe@v1', label: 'Wipe cause', category: 'MOMENTS', status: 'ghost', api: null,
    stage: 'Derived · 06', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'high', vizFit: 'med',
    builtFrom: ['Replay'], feeds: ['Evidence item'],
    description: 'Death-event classification: Systemic / Subgroup / Individual / Mixed with a confidence badge. WipeClassifier is ported and tested; needs /api/classify (phase 2).',
  },

  // ── EVENTS ───────────────────────────────────────────────────────────────
  {
    id: 'events.raw@v1', label: 'Raw event', category: 'EVENTS', status: 'ghost', api: null,
    badge: 'RAW',
    stage: 'Lex · 01', truth: 'RAW', confidence: 'high', stream: 'full',
    promptFit: 'low', vizFit: 'low',
    builtFrom: [], feeds: ['Replay event', 'Moment (view)'],
    description: 'The canonical event stream as lexed from the combat log. Too wide to prompt with directly — everything else is built from it.',
  },

  // ── REPLAY ───────────────────────────────────────────────────────────────
  {
    id: 'replay.pull@v1', label: 'Replay', category: 'REPLAY', status: 'ghost', api: null,
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'high', stream: 'trim',
    promptFit: 'low', vizFit: 'high',
    builtFrom: ['Raw event'], feeds: ['Pulse (six signals)', 'Reaction spread (player scores)', 'Cohesion graph'],
    description: 'Per-pull positional replay frames (the substrate the movement math walks). Visualization-first; not a prompt slice.',
  },

  // ── MOVEMENT ─────────────────────────────────────────────────────────────
  {
    id: 'signals.pull@v1', label: 'Pulse (six signals)', category: 'MOVEMENT', status: 'live', api: 'signals',
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'high', vizFit: 'high',
    sliceOptions: {
      scope: ['raid'],
      rep: ['timeline', 'snapshot'],          // timeline = per-second series included (real)
      agg: ['none', 'aggregates'],            // aggregates = whole-pull numbers only (real)
      time: ['whole pull', 'around deaths'],  // around deaths = ±10 s windows (real)
    },
    sliceDefaults: { scope: 'raid', rep: 'timeline', agg: 'none', time: 'whole pull' },
    builtFrom: ['Replay'], feeds: ['Pull diff', 'Evidence item'],
    description: 'The six-signal diagnostic pack per pull: spacing, healer coverage, deaths/sec, followership, entropy, HP variance — per-second series, snap markers, whole-pull aggregates. The RaidUI DiagStrip port.',
  },
  {
    id: 'affinity.night@v1', label: 'Cohesion graph', category: 'MOVEMENT', status: 'live', api: 'affinity',
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'high', vizFit: 'high',
    sliceOptions: {
      scope: ['raid'],
      rep: ['groups', 'matrix'],              // groups = clusters + coverage gaps; matrix adds pair stats (real)
      agg: ['none'],
      time: ['whole night'],                  // night-scoped artifact — pulls fan in
    },
    sliceDefaults: { scope: 'raid', rep: 'groups', agg: 'none', time: 'whole night' },
    builtFrom: ['Replay'], feeds: ['Evidence item'],
    description: 'Who actually moves together: pairwise co-travel affinity across every pull this night, cut into movement groups, with role-aware coverage gaps (healer out of range, tanks not swapping, isolated ranged).',
  },
  {
    id: 'players.pull@v1', label: 'Reaction spread (player scores)', category: 'MOVEMENT', status: 'live', api: 'players',
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'high', vizFit: 'high',
    sliceOptions: {
      scope: ['raid', 'leaders'],             // leaders = top 3 by composite (real)
      rep: ['table'],
      agg: ['none'],
      time: ['whole pull'],
    },
    sliceDefaults: { scope: 'raid', rep: 'table', agg: 'none', time: 'whole pull' },
    builtFrom: ['Replay'], feeds: ['Evidence item'],
    description: 'Per-player role-weighted 0–100 scores (movement / damage / survival / awareness) composed into a composite with a behavioral archetype. Ranks within a pull; damage is a lower bound (retained top-decile events).',
  },
  {
    id: 'shape.density@v1', label: 'Shape', category: 'MOVEMENT', status: 'ghost', api: null,
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'high', stream: 'trim',
    promptFit: 'low', vizFit: 'high',
    builtFrom: ['Replay'], feeds: [],
    description: 'The long-exposure density heatmap of a pull. Already served by /api/shape/density and rendered on the Shape tab — a cheap phase-1.5 flip once a slice serialization makes sense.',
  },
  {
    id: 'signals.followership@v1', label: 'Followership', category: 'MOVEMENT', status: 'ghost', api: null,
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'med', vizFit: 'high',
    builtFrom: ['Pulse (six signals)'], feeds: [],
    description: 'The followership signal broken out as its own slice (how together the raid moves). Today it rides inside Pulse; a per-signal slice is a phase-2 refinement.',
  },
  {
    id: 'meters.movement@v1', label: 'Movement meters', category: 'MOVEMENT', status: 'ghost', api: null,
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'med', vizFit: 'high',
    builtFrom: ['Replay'], feeds: [],
    description: 'Distance / speed / stationary leaderboard plus the wipes-vs-kills movement contrast. Already embedded in the /api/affinity payload (meters, metersByOutcome) — a cheap phase-1.5 flip.',
  },
  {
    id: 'coverage.timeline@v1', label: 'Coverage timeline', category: 'MOVEMENT', status: 'ghost', api: null,
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'med', vizFit: 'high',
    builtFrom: ['Replay'], feeds: [],
    description: 'Per-frame raid/tank healer-coverage % with snap markers. CoverageTimeline is ported and tested; needs /api/coverage (phase 2).',
  },
  {
    id: 'segments.formation@v1', label: 'Formation segments', category: 'MOVEMENT', status: 'ghost', api: null,
    stage: 'Replay · 04', truth: 'DERIVED', confidence: 'med', stream: 'trim',
    promptFit: 'med', vizFit: 'high',
    builtFrom: ['Replay'], feeds: [],
    description: 'Formation-stable periods within a pull (stacked / split / dispersed strip). FormationSegments is ported and tested; needs /api/segments (phase 2).',
  },

  // ── MECHANICS ────────────────────────────────────────────────────────────
  {
    id: 'mechanic.trigger@v1', label: 'Mechanic trigger', category: 'MECHANICS', status: 'ghost', api: null,
    stage: 'Parse · 02', truth: 'RAW', confidence: 'med', stream: 'widened',
    promptFit: 'med', vizFit: 'med',
    builtFrom: ['Raw event'], feeds: ['Moment (view)'],
    description: 'Boss cast / aura events that open a mechanic window. Arrives with the widened categorical stream tie-in.',
  },
  {
    id: 'mechanic.fired@v1', label: 'Mechanic fired', category: 'MECHANICS', status: 'ghost', api: null,
    stage: 'Parse · 02', truth: 'RAW', confidence: 'med', stream: 'widened',
    promptFit: 'med', vizFit: 'med',
    builtFrom: ['Raw event'], feeds: ['Moment (view)'],
    description: 'The mechanic actually resolving (damage / aura application). Pairs with Mechanic trigger.',
  },

  // ── PROGRESSION ──────────────────────────────────────────────────────────
  {
    id: 'diff.pulls@v1', label: 'Pull diff', category: 'PROGRESSION', status: 'live', api: 'diff',
    stage: 'Derived · 06', truth: 'DERIVED', confidence: 'high', stream: 'derived',
    promptFit: 'high', vizFit: 'high',
    sliceOptions: {
      scope: ['two pulls'],
      rep: ['table'],
      agg: ['none'],
      time: ['two pulls'],
    },
    sliceDefaults: { scope: 'two pulls', rep: 'table', agg: 'none', time: 'two pulls' },
    builtFrom: ['Reconciled pull', 'Pulse (six signals)'], feeds: ['Evidence item'],
    description: 'Deterministic two-pull comparison: deaths, end-HP%, duration, outcome from session data plus five replay-signal metrics. "This pull vs your best" — pick the compare pull in Properties.',
  },
  {
    id: 'progression.pull@v1', label: 'Reconciled pull', category: 'PROGRESSION', status: 'ghost', api: null,
    stage: 'Reconcile · 05', truth: 'RECONCILED', confidence: 'high', stream: 'full',
    promptFit: 'high', vizFit: 'med',
    builtFrom: ['Raw event'], feeds: ['Pull diff', 'Reconciled encounter'],
    description: 'One pull as the reconciler settled it: outcome, deaths, duration, boss end-HP%. The session-metadata backbone.',
  },
  {
    id: 'progression.encounter@v1', label: 'Reconciled encounter', category: 'PROGRESSION', status: 'ghost', api: null,
    stage: 'Reconcile · 05', truth: 'RECONCILED', confidence: 'high', stream: 'full',
    promptFit: 'high', vizFit: 'med',
    builtFrom: ['Reconciled pull'], feeds: [],
    description: 'A boss-night rollup of its pulls. The Trends/Roster substrate, not yet a slice.',
  },
  {
    id: 'progression.phase@v1', label: 'Phase reached', category: 'PROGRESSION', status: 'ghost', api: null,
    stage: 'Derived · 06', truth: 'DERIVED', confidence: 'med', stream: 'widened',
    promptFit: 'high', vizFit: 'med',
    builtFrom: ['Raw event'], feeds: [],
    description: 'How deep into the fight each pull got, in phases rather than HP%. Needs phase-transition detection at a host endpoint.',
  },
]

export function getObject(id) {
  return KNOWLEDGE_OBJECTS.find((o) => o.id === id) || null
}

export function liveObjects() {
  return KNOWLEDGE_OBJECTS.filter((o) => o.status === 'live')
}
