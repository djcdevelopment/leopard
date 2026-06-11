import { describe, it, expect } from 'vitest'
import { buildCareerLens, CAREER_PALETTE, CAREER_DEFAULT_IDS } from './lens.js'
import { sha256hex } from './context.js'

// ---------------------------------------------------------------------------
// Fixture — one all-time career row as /api/career serves it.
// ---------------------------------------------------------------------------
const BOSS = {
  name: 'Fyrakk the Blazing',
  difficulty: 'Heroic',
  attempts: 47,
  kills: 0,
  killed: false,
  bestPct: 12.345,
  direction: 'improving',
  arc: [45, 42, 38, 35, 31, 22, 14, 12.3],
  totalTimeMs: 47 * 6 * 60000, // 282 min
  firstSeen: '2026-03-01',
  lastSeen: '2026-04-18',
}

const ALL_IDS = new Set(CAREER_PALETTE.map((p) => p.id))

describe('buildCareerLens', () => {
  it('emits one <property> element per selected property with raw values', () => {
    const { xml, displayItems, propertyCount } = buildCareerLens(BOSS, ALL_IDS)
    expect((xml.match(/<property /g) || []).length).toBe(CAREER_PALETTE.length)
    expect(propertyCount).toBe(CAREER_PALETTE.length)
    expect(displayItems.length).toBe(propertyCount)
    expect(xml).toContain('<property id="roster.attempts@v1" exact="true" confidence="1.00" source="CareerRoster">47</property>')
    expect(xml).toContain('>no</property>') // killed=false serializes as raw "no"
    expect(xml).toContain('>12.3</property>') // bestPct toFixed(1)
  })

  it('properties appear in canonical sorted-by-id order regardless of Set insertion order', () => {
    const shuffled = new Set(['roster.lastSeen@v1', 'roster.attempts@v1', 'roster.direction@v1'])
    const { xml } = buildCareerLens(BOSS, shuffled)
    const ids = [...xml.matchAll(/<property id="([^"]+)"/g)].map((m) => m[1])
    expect(ids).toEqual(['roster.attempts@v1', 'roster.direction@v1', 'roster.lastSeen@v1'])
  })

  it('omitted properties land in <explicitlyOmitted> and emit no <property> element', () => {
    const selected = new Set([...ALL_IDS].filter((id) => id !== 'roster.totalTimeMs@v1'))
    const { xml } = buildCareerLens(BOSS, selected)
    expect(xml).not.toContain('roster.totalTimeMs@v1')
    expect(xml).toMatch(/<explicitlyOmitted>[^<]*Total time on boss[^<]*<\/explicitlyOmitted>/)
    // and the selected list names what's in
    expect(xml).toMatch(/<selected>[^<]*Total pulls[^<]*<\/selected>/)
  })

  it('empty omission set serializes as "none"', () => {
    const { xml } = buildCareerLens(BOSS, ALL_IDS)
    expect(xml).toContain('<explicitlyOmitted>none</explicitlyOmitted>')
  })

  it('digest attribute is the SHA-256 of the inner XML (root excluded)', () => {
    const { xml } = buildCareerLens(BOSS, CAREER_DEFAULT_IDS)
    const digest = xml.match(/digest="sha256:([0-9a-f]{64})"/)?.[1]
    expect(digest).toBeTruthy()
    const lines = xml.split('\n')
    const inner = lines.slice(1, -1).join('\n') // strip <context …> and </context>
    expect(sha256hex(inner)).toBe(digest)
  })

  it('is deterministic: same inputs, same bytes', () => {
    const a = buildCareerLens(BOSS, CAREER_DEFAULT_IDS)
    const b = buildCareerLens(BOSS, new Set(CAREER_DEFAULT_IDS))
    expect(a.xml).toBe(b.xml)
  })

  it('a selection change changes the digest', () => {
    const a = buildCareerLens(BOSS, ALL_IDS)
    const b = buildCareerLens(BOSS, new Set([...ALL_IDS].filter((id) => id !== 'roster.kills@v1')))
    expect(a.xml.match(/digest="([^"]+)"/)[1]).not.toBe(b.xml.match(/digest="([^"]+)"/)[1])
  })

  it('provenance: exact properties carry exact="true"; derived carry derivedFrom', () => {
    const { xml } = buildCareerLens(BOSS, ALL_IDS)
    expect(xml).toContain('<property id="roster.kills@v1" exact="true"')
    expect(xml).toMatch(/<property id="roster\.bestPct@v1" derived="true" derivedFrom="raid\.pull\.bossEndPctHp@v1"/)
    expect(xml).toMatch(/<property id="roster\.direction@v1" derived="true" derivedFrom="raid\.pull\.bossEndPctHp@v1"/)
  })

  it('direction confidence grows with attempts, capped at 0.95', () => {
    const sel = new Set(['roster.direction@v1'])
    const at = (attempts) =>
      buildCareerLens({ ...BOSS, attempts }, sel).xml.match(/confidence="([\d.]+)"/)[1]
    expect(at(3)).toBe('0.30')   // 3/10
    expect(at(47)).toBe('0.95')  // capped
  })

  it('null raw values are skipped from both XML and displayItems, and propertyCount tracks', () => {
    const sparse = { ...BOSS, bestPct: null, direction: null, arc: null }
    const { xml, displayItems, propertyCount } = buildCareerLens(sparse, ALL_IDS)
    expect(xml).not.toContain('roster.bestPct@v1')
    expect(xml).not.toContain('roster.direction@v1')
    expect(xml).not.toContain('roster.arc@v1')
    expect(displayItems.some((d) => d.id === 'roster.bestPct@v1')).toBe(false)
    expect(propertyCount).toBe(CAREER_PALETTE.length - 3)
    expect(xml).toContain(`propertyCount="${propertyCount}"`)
  })

  it('displayItems pair every property with its human-readable value', () => {
    const { displayItems } = buildCareerLens(BOSS, ALL_IDS)
    const byId = Object.fromEntries(displayItems.map((d) => [d.id, d]))
    expect(byId['roster.attempts@v1'].valueLabel).toBe('47 pulls')
    expect(byId['roster.killed@v1'].valueLabel).toBe('not yet')
    expect(byId['roster.bestPct@v1'].valueLabel).toBe('12.3% boss HP')
    expect(byId['roster.arc@v1'].valueLabel).toBe('8 pulls of data')
    expect(byId['roster.totalTimeMs@v1'].valueLabel).toBe('282 min')
    for (const d of displayItems) expect(d.label).toBeTruthy()
  })

  it('escapes XML-hostile boss names in attributes and content', () => {
    const hostile = { ...BOSS, name: 'Vexie & the <Geargrinders> "MK II"' }
    const { xml } = buildCareerLens(hostile, CAREER_DEFAULT_IDS)
    expect(xml).toContain('name="Vexie &amp; the &lt;Geargrinders&gt; &quot;MK II&quot;"')
    expect(xml).not.toMatch(/<boss name="[^"]*&(?!amp;|lt;|gt;|quot;)/)
  })
})
