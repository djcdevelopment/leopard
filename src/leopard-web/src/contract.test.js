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
