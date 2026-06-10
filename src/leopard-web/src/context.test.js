import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import { ContextBuilder, SCHEMA_VERSION } from './context.js'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------
const NIGHT_TEXT = [
  '# Amirdrassil (Heroic) - 2026-04-18 - 20 players',
  'RESULT: 2 bosses KILLED, 1 boss IN PROGRESS.',
  '',
  '## Bosses killed (2)',
  '- Gnarlroot - killed in 04:12, 3 deaths',
  '- Igira the Cruel - killed in 06:33, 7 deaths',
  '',
  '## In progress: Volcoross - 8 wipes',
  '- Deaths per pull (1..8): 4,6,5,8,9,7,6,5',
  '- Death trend: deaths held roughly steady (~6 per pull; peak 9)',
  '- Best progress: lowest boss HP reached was 34%',
  '',
  "_(Box score computed by Leopard from Tempo's parser. Every figure above is exact - reflect on these facts, do not infer beyond them.)_",
].join('\n')

const CAREER_TEXT = [
  '# Fyrakk the Blazing (Heroic) - all-time career',
  'RESULT: 47 attempt(s) across 6 night(s), not yet killed.',
  '',
  '## The arc',
  '- First pulled 2026-03-01, most recently 2026-04-18.',
  '- Best progress: lowest boss HP reached was 12% (not yet killed).',
  '- Direction: improving (best progress early ~38% -> recently ~14%, closer to a kill).',
  '- Deaths: ~11 per pull (peak 22).',
  '- Progress over time (boss %HP at end, oldest->newest, 0 = kill): 45,42,38,35,40,31,28,25,22,18,14,12',
  '',
  "_(Career summary computed by Leopard from Tempo's parser across every parsed night. Every figure above is exact - reflect on these facts, do not infer beyond them.)_",
].join('\n')

const TREND_ENC = {
  encounterId: 'enc-fyrakk-heroic',
  encounterName: 'Fyrakk the Blazing',
  difficulty: 'Heroic',
  defaultWindow: 6,
  windows: {
    6: {
      windowSize: 6,
      ruleRows: [
        { label: 'Kills', value: '0', dir: 'flat', delta: '0' },
        { label: 'Avg deaths', value: '11.2', dir: 'worse', delta: '+2.1' },
        { label: 'Best progress %', value: '14', dir: 'better', delta: '-4' },
        { label: 'Avg duration', value: '07:23', dir: 'worse', delta: '+0:44' },
      ],
    },
  },
  coherences: {
    6: {
      points: [
        { followershipMean: 0.682, entropyMean: 1.531, peakSpeed: 14.2 },
        { followershipMean: 0.701, entropyMean: 1.487, peakSpeed: 13.9 },
      ],
    },
  },
}

// Trend enc with no coherence data (advanced combat logging off)
const TREND_ENC_NO_COH = {
  encounterName: 'Volcoross',
  difficulty: 'Heroic',
  defaultWindow: 6,
  windows: { 6: { windowSize: 4, ruleRows: [{ label: 'Kills', value: '0', dir: 'flat', delta: '0' }] } },
  coherences: { 6: null },
}

// ---------------------------------------------------------------------------
// Night zoom (markdown blob)
// ---------------------------------------------------------------------------
describe('CanonicalContext — night zoom (text)', () => {
  const ctx = new ContextBuilder().setText(NIGHT_TEXT, 'this raid night').freeze()

  it('render() text is byte-identical to serialize()', () => {
    const { container } = render(ctx.render())
    expect(container.textContent).toBe(ctx.serialize())
  })

  it('determinism: same inputs → same serialize() and digest().sha256', () => {
    const ctx2 = new ContextBuilder().setText(NIGHT_TEXT, 'this raid night').freeze()
    expect(ctx2.serialize()).toBe(ctx.serialize())
    expect(ctx2.digest().sha256).toBe(ctx.digest().sha256)
  })

  it('frozen value rejects mutation', () => {
    expect(() => { ctx.serialize = () => 'hacked' }).toThrow()
  })

  it('digest carries schemaVersion and propertyCount', () => {
    const d = ctx.digest()
    expect(d.schemaVersion).toBe(SCHEMA_VERSION)
    expect(typeof d.propertyCount).toBe('number')
    expect(typeof d.sha256).toBe('string')
    expect(d.sha256).toHaveLength(64)
  })

  it('scopeLabel is baked in', () => {
    expect(ctx.scopeLabel).toBe('this raid night')
  })
})

// ---------------------------------------------------------------------------
// Career zoom (markdown blob)
// ---------------------------------------------------------------------------
describe('CanonicalContext — career zoom (text)', () => {
  const label = 'Fyrakk the Blazing (Heroic) — all-time career'
  const ctx = new ContextBuilder().setText(CAREER_TEXT, label).freeze()

  it('render() text is byte-identical to serialize()', () => {
    const { container } = render(ctx.render())
    expect(container.textContent).toBe(ctx.serialize())
  })

  it('determinism', () => {
    const ctx2 = new ContextBuilder().setText(CAREER_TEXT, label).freeze()
    expect(ctx2.digest().sha256).toBe(ctx.digest().sha256)
  })

  it('different text → different sha256', () => {
    const ctx3 = new ContextBuilder().setText(NIGHT_TEXT, label).freeze()
    expect(ctx3.digest().sha256).not.toBe(ctx.digest().sha256)
  })
})

// ---------------------------------------------------------------------------
// Trend zoom (structured enc object)
// ---------------------------------------------------------------------------
describe('CanonicalContext — trend zoom (setTrend)', () => {
  const ctx = new ContextBuilder().setTrend(TREND_ENC).freeze()

  it('render() text is byte-identical to serialize()', () => {
    const { container } = render(ctx.render())
    expect(container.textContent).toBe(ctx.serialize())
  })

  it('determinism: same enc → same serialize() and digest()', () => {
    const ctx2 = new ContextBuilder().setTrend(TREND_ENC).freeze()
    expect(ctx2.serialize()).toBe(ctx.serialize())
    expect(ctx2.digest().sha256).toBe(ctx.digest().sha256)
  })

  it('serialized text contains encounter name and rule row labels', () => {
    const text = ctx.serialize()
    expect(text).toContain('Fyrakk the Blazing')
    expect(text).toContain('Avg deaths')
    expect(text).toContain('Best progress %')
    expect(text).toContain('Followership')
  })

  it('propertyCount reflects rule rows + coherence signals', () => {
    // 4 rule rows + 3 coherence signals (fol/ent/spd all present)
    expect(ctx.digest().propertyCount).toBe(7)
  })

  it('no-coherence enc serializes without crashing', () => {
    const ctx2 = new ContextBuilder().setTrend(TREND_ENC_NO_COH).freeze()
    const { container } = render(ctx2.render())
    expect(container.textContent).toContain('advanced combat logging off')
    expect(container.textContent).toBe(ctx2.serialize())
  })

  it('scopeLabel includes encounter name', () => {
    expect(ctx.scopeLabel).toContain('Fyrakk the Blazing')
  })
})

// ---------------------------------------------------------------------------
// SHA-256 correctness — validate against Node built-in
// ---------------------------------------------------------------------------
describe('SHA-256 correctness', () => {
  it('digest().sha256 matches Node crypto for night text', async () => {
    const { createHash } = await import('crypto')
    const ctx = new ContextBuilder().setText(NIGHT_TEXT).freeze()
    const expected = createHash('sha256').update(NIGHT_TEXT, 'utf8').digest('hex')
    expect(ctx.digest().sha256).toBe(expected)
  })

  it('digest().sha256 matches Node crypto for empty string', async () => {
    const { createHash } = await import('crypto')
    const ctx = new ContextBuilder().setText('').freeze()
    const expected = createHash('sha256').update('', 'utf8').digest('hex')
    expect(ctx.digest().sha256).toBe(expected)
    // Well-known SHA-256 of empty string
    expect(ctx.digest().sha256).toBe('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855')
  })
})

// ---------------------------------------------------------------------------
// Builder contract
// ---------------------------------------------------------------------------
describe('ContextBuilder contract', () => {
  it('freeze() throws if no content was set', () => {
    expect(() => new ContextBuilder().freeze()).toThrow()
  })

  it('builder is chainable', () => {
    const ctx = new ContextBuilder().setText('x').freeze()
    expect(ctx.serialize()).toBe('x')
  })
})
