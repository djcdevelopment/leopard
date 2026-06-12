import { describe, it, expect } from 'vitest'
import { buildContract, serializeSlice, estimateTokens, coverageLabel, esc } from './contract.js'
import { getObject } from './knowledge.js'

// ---------------------------------------------------------------------------
// Fixtures — a tiny synthetic night with one boss and two pulls.
// ---------------------------------------------------------------------------
const SIG = (pullId) => ({
  pullId,
  durationSec: 6,
  healerRangeYd: 30,
  healerCount: 2,
  signals: {
    spacing: { values: [10, 11, null, 12, 13, 14], peak: { value: 10, atSec: 0 }, unit: 'yd' },
    coverage: { values: [1, 0.9, 0.8, 0.5, 0.9, 1], peak: { value: 0.5, atSec: 3 }, unit: '%' },
    deathsPerSec: { values: [0, 0, 1, 0, 0, 0], peak: { value: 1, atSec: 2 }, unit: '' },
    followership: { values: [0.2, 0.3, 0.4, 0.3, 0.2, 0.1], peak: { value: 0.4, atSec: 2 }, unit: '' },
    entropy: { values: [0.1, 0.1, 0.6, 0.2, 0.1, 0.1], peak: { value: 0.6, atSec: 2 }, unit: '' },
    hpVariance: { values: [0.05, 0.06, 0.2, 0.1, 0.05, 0.04], peak: { value: 0.2, atSec: 2 }, unit: '' },
  },
  snaps: [{ atSec: 3, dropPct: 30 }],
  aggregates: { coverageAvg: 0.85, coverageMin: 0.5, spacingTightest: 1.5, fragileSec: 1, snapCount: 1, deathsTotal: 1, durationSec: 6 },
})

const NIGHT = {
  signals: {
    encounters: [{
      encounterId: 'enc-1', encounterName: 'Vexie & the Geargrinders', difficulty: 'Mythic',
      pulls: [
        { pullId: 'pull-6', n: 6, outcome: 'wipe', signals: SIG('pull-6') },
        { pullId: 'pull-7', n: 7, outcome: 'wipe', signals: SIG('pull-7') },
      ],
    }],
  },
  players: {
    encounters: [{
      encounterId: 'enc-1', encounterName: 'Vexie & the Geargrinders', difficulty: 'Mythic',
      pulls: [{
        pullId: 'pull-7', n: 7, outcome: 'wipe',
        scores: [
          { entityId: 'e1', displayName: 'Aria', role: 'Healer', movementScore: 70, damageScore: 30, survivalScore: 100, awarenessScore: 85, compositeScore: 78, archetype: 'support_pillar' },
          { entityId: 'e2', displayName: 'Bork', role: 'Tank', movementScore: 60, damageScore: 40, survivalScore: 90, awarenessScore: 80, compositeScore: 74, archetype: 'anchor' },
          { entityId: 'e3', displayName: 'Cind', role: 'RangedDps', movementScore: 80, damageScore: 90, survivalScore: 20, awarenessScore: 30, compositeScore: 60, archetype: 'fragile_damage' },
          { entityId: 'e4', displayName: 'Dorn', role: 'MeleeDps', movementScore: 55, damageScore: 65, survivalScore: 70, awarenessScore: 50, compositeScore: 59, archetype: 'default' },
        ],
      }],
    }],
  },
  affinity: {
    participants: [
      { id: 'p1', name: 'Aria', role: 'Healer', pulls: 2 },
      { id: 'p2', name: 'Bork', role: 'Tank', pulls: 2 },
      { id: 'p3', name: 'Cind', role: 'Ranged', pulls: 2 },
    ],
    composite: [1, 0.8, 0.1, 0.8, 1, 0.3, 0.1, 0.3, 1],
    groups: [
      { groupId: 'A', members: ['Aria', 'Bork'] },
      { groupId: 'B', members: ['Cind'] },
    ],
    coverageGaps: {
      gaps: [{ kind: 'isolated-ranged', primary: { id: 'p3', name: 'Cind', role: 'Ranged' }, secondary: null, composite: 0.1, interpretation: 'Cind max pair-composite 0.10 - standing alone too much.' }],
      gapCount: 1, healersWithGaps: [], tanksWellPaired: true,
    },
    // The embedded meters.js port (the 'meters' pseudo-api reads these).
    meters: [
      // 1199.6 yd rounds to a trailing zero — the fmt regression case (must read 1200, not 12).
      { participantId: 'p2', displayName: 'Bork', role: 'Tank', totalDistanceYd: 1199.6, avgSpeedYdPerSec: 3.1, peakSpeedYdPerSec: 12.4, stationaryRatio: 0.42, movedRatio: 0.58, perPullMetrics: [] },
      { participantId: 'p1', displayName: 'Aria', role: 'Healer', totalDistanceYd: 840.2, avgSpeedYdPerSec: 2.2, peakSpeedYdPerSec: 8.9, stationaryRatio: 0.61, movedRatio: 0.39, perPullMetrics: [] },
      { participantId: 'p3', displayName: 'Cind', role: 'Ranged', totalDistanceYd: 412, avgSpeedYdPerSec: 1.4, peakSpeedYdPerSec: 30.5, stationaryRatio: 0.7, movedRatio: 0.3, perPullMetrics: [] },
    ],
    metersByOutcome: {
      wipesOnly: [], killsOnly: [],
      delta: [
        { participantId: 'p1', displayName: 'Aria', distanceDelta: 120.4, avgSpeedDelta: 0.45, peakSpeedDelta: 1.1, stationaryDelta: -0.08, wipePulls: 2, killPulls: 1 },
        { participantId: 'p3', displayName: 'Cind', distanceDelta: null, avgSpeedDelta: null, peakSpeedDelta: null, stationaryDelta: null, wipePulls: 2, killPulls: 0 },
      ],
      wipeSamples: 2, killSamples: 1,
    },
  },
  coverage: {
    encounters: [{
      encounterId: 'enc-1', encounterName: 'Vexie & the Geargrinders', difficulty: 'Mythic',
      pulls: [{
        pullId: 'pull-7', n: 7, outcome: 'wipe',
        coverage: {
          seconds: { raidPct: [100, 75, 50, 100], tankPct: [100, 100, 100, 100], flexPct: [50, 50, 25, 50], quality: [80, 70, 40, 85] },
          summary: {
            avgRaidPct: 81.25, avgTankPct: 100, avgFlexPct: 43.75, avgQualityScore: 68.75,
            minRaidPct: 50, minTankPct: 100, minQualityScore: 40, timeInFragileCoverageMs: 1000,
            snappingPoints: [{ timeMs: 2000, qualityDrop: 30, followedByDamageMs: 800 }],
          },
        },
      }],
    }],
  },
  segments: {
    encounters: [{
      encounterId: 'enc-1', encounterName: 'Vexie & the Geargrinders', difficulty: 'Mythic',
      pulls: [{
        pullId: 'pull-7', n: 7, outcome: 'wipe',
        segments: [
          { startFrameIdx: 0, endFrameIdx: 190, startMs: 0, endMs: 38000, formation: 'stacked', medianPairwiseDistanceYd: 4.2, frameCount: 190 },
          { startFrameIdx: 190, endFrameIdx: 355, startMs: 38000, endMs: 71000, formation: 'split', medianPairwiseDistanceYd: 9.8, frameCount: 165 },
        ],
        phases: 'stacked 0-38s (4yd), split 38-71s (10yd)',
      }],
    }],
  },
  classify: {
    encounters: [{
      encounterId: 'enc-1', encounterName: 'Vexie & the Geargrinders', difficulty: 'Mythic',
      pulls: [
        {
          pullId: 'pull-6', n: 6, outcome: 'wipe',
          classification: { kind: 'called-wipe', confidence: 'high', affected: [], inflectionMs: 100000, evidence: [], calledWipePattern: 'synchronized-reset', coveragePattern: null, offender: null },
          reason: null,
        },
        {
          pullId: 'pull-7', n: 7, outcome: 'wipe',
          classification: {
            kind: 'subgroup', confidence: 'med', affected: ['Cind', 'Dorn'], inflectionMs: 42000,
            evidence: [
              { signalId: 'coverage', value: 0.45, reason: 'coverage quality dropped' },
              { signalId: 'deaths-per-sec', value: 2, reason: 'deaths in a 3 s window' },
            ],
            calledWipePattern: null, coveragePattern: 'snap',
            offender: { entityId: 'e1', displayName: 'Aria', atMs: 41000 },
          },
          reason: null,
        },
        { pullId: 'pull-8', n: 8, outcome: 'wipe', classification: null, reason: 'no replay frames' },
      ],
    }],
  },
  shape: {
    encounters: [{
      encounterId: 'enc-1', encounterName: 'Vexie & the Geargrinders', difficulty: 'Mythic',
      pulls: [{
        pullId: 'pull-7', n: 7, outcome: 'wipe',
        // 4x4 grid: cell 5 = (col 1, row 1) dominates; three minor cells.
        density: {
          gridW: 4, gridH: 4,
          cells: [0, 0, 0, 0, 0, 1, 0.2, 0, 0, 0.15, 0.1, 0, 0, 0, 0, 0],
          totalSamples: 500, maxBucket: 200, arenaW: 100, arenaH: 80,
        },
      }],
    }],
  },
  diff: {
    encounterName: 'Vexie & the Geargrinders', encounterDifficulty: 'Mythic',
    left: { id: 'pull-6', n: 6 }, right: { id: 'pull-7', n: 7 },
    ruleHeadline: 'Pull #7 took 2 more deaths.', ruleSubline: '9 metrics.',
    metrics: [
      { id: 'deaths', label: 'Deaths', sev: 'warn', l: 3, r: 5, unit: '', delta: '+2', dir: 'worse', detail: 'd +2', wired: true },
      { id: 'rcov-avg', label: 'Raid coverage avg', sev: 'info', l: null, r: null, unit: '%', delta: null, dir: 'flat', detail: 'requires replay frames', wired: false },
    ],
  },
}

const FRAME = {
  pull: { pullId: 'pull-7', n: 7, outcome: 'wipe' },
  encounterName: 'Vexie & the Geargrinders',
  difficulty: 'Mythic',
  durationMs: 443080,
  nightData: NIGHT,
}

const slice = (objectId, over = {}, extra = undefined) => ({ objectId, slice: over, extra })

describe('slice-contract compiler', () => {
  it('builds the mockup-schema envelope with digest, slice count and tok attrs', () => {
    const { xml, sliceItems, totalTok, digest } = buildContract({
      ...FRAME,
      slices: [
        slice('signals.pull@v1'),
        slice('players.pull@v1'),
        slice('affinity.night@v1'),
        slice('diff.pulls@v1', {}, { comparePullId: 'pull-6' }),
      ],
    })
    expect(xml.startsWith(`<context version="1" digest="sha256:${digest}" slices="4">`)).toBe(true)
    expect(xml).toContain('<pull id="pull-7" n="7" encounter="Vexie &amp; the Geargrinders"')
    expect(xml).toContain('durationMs="443080"')
    expect((xml.match(/<slice /g) || []).length).toBe(4)
    expect(xml).toMatch(/tok="\d+"/)
    expect(sliceItems.length).toBe(4)
    expect(totalTok).toBe(estimateTokens(xml))
  })

  it('digest is stable across runs and slices are canonically sorted by id', () => {
    const a = buildContract({ ...FRAME, slices: [slice('signals.pull@v1'), slice('players.pull@v1')] })
    const b = buildContract({ ...FRAME, slices: [slice('players.pull@v1'), slice('signals.pull@v1')] })
    expect(a.digest).toBe(b.digest)
    expect(a.xml).toBe(b.xml)
    // players sorts before signals (ordinal)
    expect(a.xml.indexOf('players.pull@v1')).toBeLessThan(a.xml.indexOf('signals.pull@v1'))
  })

  it('a slice-attribute change changes the bytes and the digest', () => {
    const a = buildContract({ ...FRAME, slices: [slice('signals.pull@v1', { rep: 'timeline' })] })
    const b = buildContract({ ...FRAME, slices: [slice('signals.pull@v1', { rep: 'snapshot' })] })
    expect(a.digest).not.toBe(b.digest)
    expect(a.xml).toContain('spacing (yd):')      // timeline carries the series
    expect(b.xml).not.toContain('spacing (yd):')  // snapshot is aggregates-only
    expect(b.xml).toContain('aggregates: coverage avg 85%')
  })

  it('time="around deaths" masks the series to death windows', () => {
    const body = serializeSlice(getObject('signals.pull@v1'),
      { scope: 'raid', rep: 'timeline', agg: 'none', time: 'around deaths' },
      NIGHT, { pullId: 'pull-7' })
    // the only death is at sec 2; ±10 s covers the whole 6 s fixture
    expect(body).toContain('time bound: +/-10 s around each death (6 of 6 s kept)')
  })

  it('players scope=leaders keeps the top 3 and says what was omitted', () => {
    const body = serializeSlice(getObject('players.pull@v1'),
      { scope: 'leaders', rep: 'table', agg: 'none', time: 'whole pull' },
      NIGHT, { pullId: 'pull-7' })
    expect(body).toContain('1. Aria (Healer): composite 78')
    expect(body).toContain('3. Cind')
    expect(body).not.toContain('Dorn')
    expect(body).toContain('(1 more players omitted by scope=leaders)')
  })

  it('affinity rep=matrix adds pair stats; groups + gaps always present', () => {
    const groups = serializeSlice(getObject('affinity.night@v1'),
      { scope: 'raid', rep: 'groups', agg: 'none', time: 'whole night' }, NIGHT, { pullId: 'pull-7' })
    expect(groups).toContain('group A (2): Aria, Bork')
    expect(groups).toContain('gap [isolated-ranged]:')
    expect(groups).not.toContain('pair composite:')
    const matrix = serializeSlice(getObject('affinity.night@v1'),
      { scope: 'raid', rep: 'matrix', agg: 'none', time: 'whole night' }, NIGHT, { pullId: 'pull-7' })
    expect(matrix).toContain('pair composite: mean 0.4 across 3 pairs')
    expect(matrix).toContain('tightest pairs: Aria+Bork 0.8')
  })

  it('diff serializes wired rows with deltas and unwired rows as explicit absence', () => {
    const body = serializeSlice(getObject('diff.pulls@v1'), {}, NIGHT, { pullId: 'pull-7', extra: { comparePullId: 'pull-6' } })
    expect(body).toContain('Pull #7 took 2 more deaths.')
    expect(body).toContain('Deaths: 3 -> 5 (+2, worse)')
    expect(body).toContain('Raid coverage avg: (requires replay frames - requires replay frames)')
  })

  it('diff without a compare pull is a placeholder, never fabricated', () => {
    const body = serializeSlice(getObject('diff.pulls@v1'), {}, NIGHT, { pullId: 'pull-7', extra: {} })
    expect(body).toBe('(no compare pull selected - pick one in Properties)')
  })

  it('missing artifacts serialize as explicit absence lines', () => {
    const empty = { signals: null, players: null, affinity: null, diff: null }
    const body = serializeSlice(getObject('signals.pull@v1'),
      { scope: 'raid', rep: 'timeline', agg: 'none', time: 'whole pull' }, empty, { pullId: 'pull-7' })
    expect(body).toContain('(no replay-derived signals for this pull')
  })

  // ── phase-2 serializers ────────────────────────────────────────────────────

  it('coverage rep=timeline carries the per-second series; rep=summary drops it', () => {
    const timeline = serializeSlice(getObject('coverage.timeline@v1'),
      { scope: 'raid', rep: 'timeline', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-7' })
    expect(timeline).toContain('summary: raid coverage avg 81% / min 50% - tank avg 100% / min 100% - quality avg 69 / min 40 - fragile 1 s')
    expect(timeline).toContain('snap: quality -30 at 2s, damage followed 0.8 s later')
    expect(timeline).toContain('quality score: 80 70 40 85')
    expect(timeline).toContain('raid coverage %: 100 75 50 100')
    const summary = serializeSlice(getObject('coverage.timeline@v1'),
      { scope: 'raid', rep: 'summary', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-7' })
    expect(summary).not.toContain('quality score:')
    expect(summary).toContain('snap: quality -30 at 2s')
  })

  it('segments serialize the phase story plus per-segment buckets', () => {
    const body = serializeSlice(getObject('segments.formation@v1'),
      { scope: 'raid', rep: 'phases', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-7' })
    expect(body).toContain('phases: stacked 0-38s (4yd), split 38-71s (10yd)')
    expect(body).toContain('stacked: 0-38s - median pairwise 4.2 yd (190 frames)')
    expect(body).toContain('split: 38-71s - median pairwise 9.8 yd (165 frames)')
  })

  it('classify serializes the verdict; rep=verdict drops evidence; called-wipe gates coaching', () => {
    const full = serializeSlice(getObject('classify.wipe@v1'),
      { scope: 'raid', rep: 'verdict + evidence', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-7' })
    expect(full).toContain('verdict: subgroup collapse, confidence med, onset ~42s')
    expect(full).toContain('most implicated: Cind, Dorn')
    expect(full).toContain('evidence [coverage]: coverage quality dropped (value 0.45)')
    expect(full).toContain('coverage pattern: snap - Aria at 41s')
    const bare = serializeSlice(getObject('classify.wipe@v1'),
      { scope: 'raid', rep: 'verdict', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-7' })
    expect(bare).not.toContain('evidence [')
    expect(bare).toContain('verdict: subgroup collapse')
    const called = serializeSlice(getObject('classify.wipe@v1'),
      { scope: 'raid', rep: 'verdict + evidence', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-6' })
    expect(called).toBe('CALLED WIPE (synchronized-reset) - the raid reset on purpose; do not analyze this wipe as a failure.')
    const declined = serializeSlice(getObject('classify.wipe@v1'),
      { scope: 'raid', rep: 'verdict + evidence', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-8' })
    expect(declined).toBe('(not classified - no replay frames)')
  })

  it('meters rank by distance and survive the trailing-zero fmt case', () => {
    const body = serializeSlice(getObject('meters.movement@v1'),
      { scope: 'raid', rep: 'leaderboard', agg: 'none', time: 'whole night' }, NIGHT, { pullId: 'pull-7' })
    // 1199.6 yd must round to 1200, not collapse to 12 (the stripped-zero regression).
    expect(body).toContain('1. Bork (Tank): total 1200 yd - avg 3.1 yd/s - peak 12.4 yd/s - stationary 42%')
    expect(body).toContain('3. Cind (Ranged): total 412 yd')
  })

  it('meters agg="wipes vs kills" emits per-player deltas and skips one-sided players', () => {
    const body = serializeSlice(getObject('meters.movement@v1'),
      { scope: 'raid', rep: 'leaderboard', agg: 'wipes vs kills', time: 'whole night' }, NIGHT, { pullId: 'pull-7' })
    expect(body).toContain('wipes vs kills (2 wipe / 1 kill pulls), per-player wipe-minus-kill:')
    expect(body).toContain('Aria: distance +120 yd - avg speed +0.45 yd/s - stationary -8pp')
    expect(body).not.toContain('Cind: distance') // null delta = no kills sampled, stays silent in the delta block
  })

  it('shape serializes hotspot + concentration from the density grid', () => {
    const body = serializeSlice(getObject('shape.density@v1'),
      { scope: 'raid', rep: 'hotspots', agg: 'none', time: 'whole pull' }, NIGHT, { pullId: 'pull-7' })
    expect(body).toContain('arena ~100x80 yd, 4x4 occupancy grid, 500 position samples')
    expect(body).toContain('hotspot: cell centered (38, 30) yd held the most raid-presence')
    expect(body).toContain('concentration: half of all standing time in 1 of 4 occupied cells (25%)')
  })

  it('phase-2 slices serialize explicit absence when their artifacts are missing', () => {
    const empty = { signals: null, players: null, affinity: null, diff: null, coverage: null, segments: null, classify: null, shape: null }
    const cases = [
      ['coverage.timeline@v1', '(no coverage quality model for this pull'],
      ['segments.formation@v1', '(no formation segments for this pull'],
      ['classify.wipe@v1', '(no wipe classification for this pull'],
      ['meters.movement@v1', '(no movement meters for this night'],
      ['shape.density@v1', '(no density grid for this pull'],
    ]
    for (const [id, expected] of cases) {
      const entry = getObject(id)
      const body = serializeSlice(entry, { ...entry.sliceDefaults }, empty, { pullId: 'pull-7' })
      expect(body, id).toContain(expected)
    }
  })

  it('a nine-slice contract compiles with a stable digest', () => {
    const all = [
      slice('signals.pull@v1'), slice('players.pull@v1'), slice('affinity.night@v1'),
      slice('diff.pulls@v1', {}, { comparePullId: 'pull-6' }),
      slice('coverage.timeline@v1'), slice('segments.formation@v1'), slice('classify.wipe@v1'),
      slice('meters.movement@v1'), slice('shape.density@v1'),
    ]
    const a = buildContract({ ...FRAME, slices: all })
    const b = buildContract({ ...FRAME, slices: [...all].reverse() })
    expect(a.xml).toContain('slices="9"')
    expect((a.xml.match(/<slice /g) || []).length).toBe(9)
    expect(a.digest).toBe(b.digest)
  })

  it('escapes attribute values and payload bytes', () => {
    expect(esc('a<b>&"c"')).toBe('a&lt;b&gt;&amp;&quot;c&quot;')
    const { xml } = buildContract({ ...FRAME, slices: [slice('signals.pull@v1')] })
    expect(xml).not.toMatch(/encounter="[^"]*&(?!amp;|lt;|gt;|quot;)/)
  })

  it('estimateTokens is the chars/4 heuristic', () => {
    expect(estimateTokens('abcd')).toBe(1)
    expect(estimateTokens('abcde')).toBe(2)
    expect(estimateTokens('')).toBe(0)
  })

  it('coverage label widens with category spread', () => {
    expect(coverageLabel([])).toBe('Empty contract')
    expect(coverageLabel([slice('signals.pull@v1')])).toBe('Narrow coverage')
    expect(coverageLabel([slice('signals.pull@v1'), slice('diff.pulls@v1')])).toBe('Mixed coverage')
  })
})
