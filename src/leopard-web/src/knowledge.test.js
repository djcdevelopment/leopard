import { describe, it, expect } from 'vitest'
import { KNOWLEDGE_OBJECTS, CATEGORY_ORDER, EXPLORER_SEEDS, getObject, liveObjects } from './knowledge.js'

describe('knowledge registry', () => {
  it('every entry carries the fields the tree and Properties panel render', () => {
    for (const o of KNOWLEDGE_OBJECTS) {
      expect(o.id, o.id).toMatch(/^[a-z][a-zA-Z.]*@v\d+$/)
      expect(typeof o.label).toBe('string')
      expect(o.label.length).toBeGreaterThan(0)
      expect(['live', 'ghost']).toContain(o.status)
      expect(['RAW', 'DERIVED', 'RECONCILED']).toContain(o.truth)
      expect(typeof o.stage).toBe('string')
      expect(typeof o.stream).toBe('string')
      expect(['low', 'med', 'high']).toContain(o.confidence)
      expect(['low', 'med', 'high']).toContain(o.promptFit)
      expect(['low', 'med', 'high']).toContain(o.vizFit)
      expect(Array.isArray(o.builtFrom)).toBe(true)
      expect(Array.isArray(o.feeds)).toBe(true)
      expect(typeof o.description).toBe('string')
    }
  })

  it('ids are unique', () => {
    const ids = KNOWLEDGE_OBJECTS.map((o) => o.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('every category is in CATEGORY_ORDER', () => {
    for (const o of KNOWLEDGE_OBJECTS) expect(CATEGORY_ORDER).toContain(o.category)
  })

  it('live entries name an api fetcher and slice options; ghosts have api:null', () => {
    for (const o of KNOWLEDGE_OBJECTS) {
      if (o.status === 'live') {
        // 'meters' is the documented pseudo-key (rides the affinity payload).
        expect(['signals', 'players', 'affinity', 'diff', 'coverage', 'segments', 'classify', 'meters', 'shape', 'night', 'trends']).toContain(o.api)
        expect(o.sliceOptions).toBeTruthy()
        expect(o.sliceDefaults).toBeTruthy()
        // every default is one of its own options
        for (const [k, v] of Object.entries(o.sliceDefaults)) {
          expect(o.sliceOptions[k], `${o.id} ${k}`).toContain(v)
        }
      } else {
        expect(o.api).toBeNull()
      }
    }
  })

  it('the phase-1 ports, phase-2 flips, and the over-time slices are live', () => {
    expect(liveObjects().map((o) => o.id).sort()).toEqual([
      'affinity.night@v1', 'classify.wipe@v1', 'coverage.timeline@v1', 'diff.pulls@v1',
      'meters.movement@v1', 'players.pull@v1', 'progression.encounter@v1',
      'segments.formation@v1', 'shape.density@v1', 'signals.pull@v1', 'trend.window@v1',
    ])
  })

  it('getObject resolves and misses cleanly', () => {
    expect(getObject('signals.pull@v1')?.label).toBe('Pulse (six signals)')
    expect(getObject('nope@v1')).toBeNull()
  })

  it('seed questions exist for the QUESTIONS section', () => {
    expect(EXPLORER_SEEDS.length).toBeGreaterThanOrEqual(3)
    for (const q of EXPLORER_SEEDS) expect(q.endsWith('?')).toBe(true)
  })
})
