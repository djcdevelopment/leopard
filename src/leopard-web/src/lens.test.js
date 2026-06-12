import { describe, it, expect } from 'vitest'
import {
  buildCareerLens, CAREER_PALETTE, CAREER_DEFAULT_IDS,
  buildNightLens, NIGHT_PALETTE, NIGHT_DEFAULT_IDS,
} from './lens.js'
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

// ---------------------------------------------------------------------------
// Night lens — the structured box score (.night.v1.json) composed per property.
// ---------------------------------------------------------------------------
const NIGHT = {
  zone: 'Liberation of Undermine',
  date: '2026-06-06',
  difficulty: 'Heroic',
  playerCount: 20,
  bossesKilled: 1,
  bossesInProgress: 1,
  encounters: [
    {
      name: 'Vexie and the Geargrinders', difficulty: 'Heroic', killed: true, pullCount: 1,
      killDurationMs: 253000, killDeaths: 3,
      deathsPerPull: [3], deathTrend: null, executePulls: [], bestProgressPct: 0,
      fastWipePulls: [], longestPulls: [{ n: 1, durationMs: 253000 }],
      pulls: [{ n: 1, outcome: 'kill', deaths: 3, durationMs: 253000, bossEndPctHp: 0 }],
    },
    {
      name: 'Mug\'Zee', difficulty: 'Heroic', killed: false, pullCount: 5,
      killDurationMs: null, killDeaths: null,
      deathsPerPull: [8, 12, 9, 6, 4],
      deathTrend: 'deaths fell over the night (early ~8 -> later ~4 per pull)',
      executePulls: [], bestProgressPct: 23.4,
      fastWipePulls: [2], longestPulls: [{ n: 5, durationMs: 312000 }, { n: 4, durationMs: 280000 }],
      pulls: [
        { n: 1, outcome: 'wipe', deaths: 8, durationMs: 180000, bossEndPctHp: 61.2 },
        { n: 2, outcome: 'wipe', deaths: 12, durationMs: 25000, bossEndPctHp: 88.0 },
        { n: 3, outcome: 'wipe', deaths: 9, durationMs: 200000, bossEndPctHp: 44.1 },
        { n: 4, outcome: 'wipe', deaths: 6, durationMs: 280000, bossEndPctHp: 30.0 },
        { n: 5, outcome: 'wipe', deaths: 4, durationMs: 312000, bossEndPctHp: 23.4 },
      ],
    },
  ],
}

const ALL_NIGHT_IDS = new Set(NIGHT_PALETTE.map((p) => p.id))

describe('buildNightLens', () => {
  it('emits night-scoped properties once and encounter-scoped ones nested per matching boss', () => {
    const { xml, displayItems } = buildNightLens(NIGHT, ALL_NIGHT_IDS)
    expect(xml).toContain('<property id="raid.night.zone@v1" exact="true" confidence="1.00" source="BoxScore">Liberation of Undermine</property>')
    expect(xml).toContain('<property id="raid.night.bossesKilled@v1" exact="true" confidence="1.00" source="BoxScore">1</property>')
    // the killed boss gets the kill summary, NOT the wipe-progress lines
    expect(xml).toMatch(/<encounter name="Vexie and the Geargrinders" killed="yes" pulls="1">\s*\n\s*<property id="raid\.encounter\.killSummary@v1"[^>]*>killed in 04:13, 3 deaths<\/property>/)
    expect(xml.match(/<encounter name="Vexie[^>]*>[\s\S]*?<\/encounter>/)[0]).not.toContain('deathTrend')
    // the in-progress boss gets the progress lines, NOT a kill summary
    const mug = xml.match(/<encounter name="Mug&#?\w*;?Zee[\s\S]*?<\/encounter>/) || xml.match(/<encounter name="Mug[\s\S]*?<\/encounter>/)
    expect(mug[0]).toContain('raid.pull.deaths@v1')
    expect(mug[0]).toContain('8, 12, 9, 6, 4 (pulls 1..5)')
    expect(mug[0]).toContain('lowest boss HP reached was 23%')
    expect(mug[0]).toContain('pulls 2</property>') // fast wipes
    expect(mug[0]).toContain('#5 (05:12), #4 (04:40)')
    expect(mug[0]).not.toContain('killSummary')
    // encounter-scoped display labels carry the boss name
    expect(displayItems.some((d) => d.label === 'Mug\'Zee — Death trend')).toBe(true)
  })

  it('death-trend confidence scales with the encounter pull count', () => {
    const { xml } = buildNightLens(NIGHT, new Set(['raid.encounter.deathTrend@v1']))
    expect(xml).toContain('confidence="0.50"') // 5 pulls / 10
    expect(xml).toMatch(/derived="true" derivedFrom="raid\.pull\.deaths@v1"/)
  })

  it('UserIntent records selection and omission; digest covers the inner XML', () => {
    const selected = new Set([...ALL_NIGHT_IDS].filter((id) => id !== 'raid.encounter.fastWipePulls@v1'))
    const { xml } = buildNightLens(NIGHT, selected)
    expect(xml).toMatch(/<explicitlyOmitted>[^<]*Fast wipes[^<]*<\/explicitlyOmitted>/)
    expect(xml).not.toContain('raid.encounter.fastWipePulls@v1')
    const digest = xml.match(/digest="sha256:([0-9a-f]{64})"/)?.[1]
    const inner = xml.split('\n').slice(1, -1).join('\n')
    expect(sha256hex(inner)).toBe(digest)
  })

  it('is deterministic and selection-sensitive', () => {
    const a = buildNightLens(NIGHT, NIGHT_DEFAULT_IDS)
    const b = buildNightLens(NIGHT, new Set(NIGHT_DEFAULT_IDS))
    expect(a.xml).toBe(b.xml)
    const c = buildNightLens(NIGHT, new Set([...NIGHT_DEFAULT_IDS].filter((id) => id !== 'raid.night.zone@v1')))
    expect(a.xml.match(/digest="([^"]+)"/)[1]).not.toBe(c.xml.match(/digest="([^"]+)"/)[1])
  })

  it('missing boss-HP readings state their absence explicitly (the markdown rule)', () => {
    const noHp = {
      ...NIGHT,
      encounters: [{ ...NIGHT.encounters[1], executePulls: [], bestProgressPct: null }],
    }
    const { xml } = buildNightLens(noHp, new Set(['raid.encounter.bestProgressPct@v1']))
    expect(xml).toContain('no reliable boss-HP reading was recorded')
  })

  it('execute-range pulls win over the lowest-HP line', () => {
    const exec = {
      ...NIGHT,
      encounters: [{ ...NIGHT.encounters[1], executePulls: [4, 5] }],
    }
    const { xml } = buildNightLens(exec, new Set(['raid.encounter.bestProgressPct@v1']))
    expect(xml).toContain('reached boss 0% (execute range) on pulls 4, 5')
  })

  it('null/empty values skip their lines and propertyCount tracks displayItems', () => {
    const sparse = { ...NIGHT, date: '', playerCount: 0 }
    const { xml, displayItems, propertyCount } = buildNightLens(sparse, ALL_NIGHT_IDS)
    expect(xml).not.toContain('raid.night.date@v1')
    expect(xml).not.toContain('raid.night.playerCount@v1')
    expect(propertyCount).toBe(displayItems.length)
    expect(xml).toContain(`propertyCount="${propertyCount}"`)
  })

  it('default selection omits only the date', () => {
    expect([...ALL_NIGHT_IDS].filter((id) => !NIGHT_DEFAULT_IDS.has(id))).toEqual(['raid.night.date@v1'])
  })
})
