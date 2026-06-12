// Career + night lenses for the query builder / lens composer.
// Pure logic — no React. Each lens takes a structured artifact and a Set of selected property
// IDs and produces { xml, displayItems, propertyCount }.
//
// xml         = the exact bytes sent to the model (versioned envelope + UserIntent + provenance)
// displayItems = [{ id, label, valueLabel }] — human-readable lines for "What Leopard knows"
//
// Invariant: xml is derived from the same (boss, selectedIds) pair that produces displayItems,
// so the display list and the model payload cannot drift from each other.

import { sha256hex } from './context.js'

// Stable property palette for the career (roster) lens. IDs match docs/property-inventory.md.
export const CAREER_PALETTE = [
  { id: 'roster.attempts@v1',    label: 'Total pulls',              exact: true,  confidence: 1.0, source: 'CareerRoster' },
  { id: 'roster.kills@v1',       label: 'Kill count',               exact: true,  confidence: 1.0, source: 'CareerRoster' },
  { id: 'roster.killed@v1',      label: 'Ever killed',              exact: true,  confidence: 1.0, source: 'CareerRoster' },
  { id: 'roster.bestPct@v1',     label: 'Best boss HP reached (%)', exact: false, confidence: 1.0, derivedFrom: 'raid.pull.bossEndPctHp@v1', source: 'CareerRoster' },
  { id: 'roster.direction@v1',   label: 'Trend direction',          exact: false, derivedFrom: 'raid.pull.bossEndPctHp@v1', source: 'CareerRoster' },
  { id: 'roster.arc@v1',         label: 'Progress arc (all pulls)', exact: true,  confidence: 1.0, source: 'CareerRoster' },
  { id: 'roster.totalTimeMs@v1', label: 'Total time on boss',       exact: true,  confidence: 1.0, source: 'CareerRoster' },
  { id: 'roster.firstSeen@v1',   label: 'First pulled',             exact: true,  confidence: 1.0, source: 'CareerRoster' },
  { id: 'roster.lastSeen@v1',    label: 'Most recently pulled',     exact: true,  confidence: 1.0, source: 'CareerRoster' },
]

// Default: everything except total time (less useful for a first read)
export const CAREER_DEFAULT_IDS = new Set(
  CAREER_PALETTE.map(p => p.id).filter(id => id !== 'roster.totalTimeMs@v1')
)

// Raw value for XML serialization
function getRawValue(boss, id) {
  switch (id) {
    case 'roster.attempts@v1':    return boss.attempts
    case 'roster.kills@v1':       return boss.kills
    case 'roster.killed@v1':      return boss.killed ? 'yes' : 'no'
    case 'roster.bestPct@v1':     return boss.bestPct != null ? boss.bestPct.toFixed(1) : null
    case 'roster.direction@v1':   return boss.direction || null
    case 'roster.arc@v1':         return boss.arc ? boss.arc.map(v => Number(v).toFixed(1)).join(', ') : null
    case 'roster.totalTimeMs@v1': return boss.totalTimeMs != null ? Math.round(boss.totalTimeMs / 60000) : null
    case 'roster.firstSeen@v1':   return boss.firstSeen || null
    case 'roster.lastSeen@v1':    return boss.lastSeen || null
    default:                       return null
  }
}

// Human-readable value for the "What Leopard knows" display list
function getDisplayValue(boss, id) {
  switch (id) {
    case 'roster.attempts@v1':    return `${boss.attempts} pull${boss.attempts === 1 ? '' : 's'}`
    case 'roster.kills@v1':       return boss.kills === 1 ? '1 kill' : `${boss.kills} kills`
    case 'roster.killed@v1':      return boss.killed ? 'yes' : 'not yet'
    case 'roster.bestPct@v1':     return boss.bestPct != null ? `${boss.bestPct.toFixed(1)}% boss HP` : null
    case 'roster.direction@v1':   return boss.direction || null
    case 'roster.arc@v1':         return boss.arc ? `${boss.arc.length} pulls of data` : null
    case 'roster.totalTimeMs@v1': return boss.totalTimeMs != null ? `${Math.round(boss.totalTimeMs / 60000)} min` : null
    case 'roster.firstSeen@v1':   return boss.firstSeen || null
    case 'roster.lastSeen@v1':    return boss.lastSeen || null
    default:                       return null
  }
}

// direction confidence grows with sample size: min(attempts/10, 0.95)
function getConfidence(prop, boss) {
  if (prop.id === 'roster.direction@v1') return Math.min((boss.attempts || 0) / 10, 0.95)
  return prop.confidence ?? 1.0
}

function esc(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

// Build the versioned XML context + human-readable display list for a career lens.
// selectedIds is a Set<string> of property IDs from CAREER_PALETTE.
export function buildCareerLens(boss, selectedIds) {
  // Sort by stable ID (ordinal, culture-invariant) — canonical serialization rule #1
  const sorted = [...selectedIds].sort((a, b) => a.localeCompare(b, 'en', { sensitivity: 'variant' }))
  const allIds = CAREER_PALETTE.map(p => p.id)
  const omittedIds = allIds.filter(id => !selectedIds.has(id))

  const selectedLabels = sorted.map(id => CAREER_PALETTE.find(p => p.id === id)?.label).filter(Boolean).join(', ')
  const omittedLabels = omittedIds.map(id => CAREER_PALETTE.find(p => p.id === id)?.label).filter(Boolean).join(', ')

  const displayItems = []
  const propLines = []

  for (const id of sorted) {
    const prop = CAREER_PALETTE.find(p => p.id === id)
    if (!prop) continue
    const raw = getRawValue(boss, id)
    const display = getDisplayValue(boss, id)
    if (raw === null || raw === undefined) continue
    const conf = getConfidence(prop, boss)
    const provenanceAttr = prop.exact
      ? 'exact="true"'
      : `derived="true" derivedFrom="${esc(prop.derivedFrom)}"`
    propLines.push(
      `    <property id="${esc(id)}" ${provenanceAttr} confidence="${conf.toFixed(2)}" source="${esc(prop.source)}">${esc(String(raw))}</property>`
    )
    displayItems.push({ id, label: prop.label, valueLabel: display })
  }

  // digest is over the inner XML, not including the root element (so the attribute is stable)
  const inner = [
    `  <boss name="${esc(boss.name)}" difficulty="${esc(boss.difficulty)}">`,
    `    <UserIntent>`,
    `      <selected>${esc(selectedLabels)}</selected>`,
    `      <explicitlyOmitted>${esc(omittedLabels || 'none')}</explicitlyOmitted>`,
    `    </UserIntent>`,
    ...propLines,
    `  </boss>`,
  ].join('\n')

  const digest = sha256hex(inner)
  const xml = `<context version="1" digest="sha256:${digest}" propertyCount="${displayItems.length}">\n${inner}\n</context>`

  return { xml, displayItems, propertyCount: displayItems.length }
}

// ── Night lens ───────────────────────────────────────────────────────────────
// Composes the structured box score (.night.v1.json — BoxScore.BuildJson) the way the career
// lens composes a roster row. Night-scoped properties serialize once; encounter-scoped ones
// serialize per matching encounter, nested in <encounter> blocks. Same canonical rules:
// sort-by-id, esc, digest over the inner XML with the root excluded.

// Stable palette — IDs match docs/property-inventory.md ("Box Score artifact" + the
// knowledge-object namespace section). scope: 'night' | 'killed' | 'inprogress'.
export const NIGHT_PALETTE = [
  { id: 'raid.night.zone@v1',                label: 'Zone',                     scope: 'night',      exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.night.date@v1',                label: 'Date',                     scope: 'night',      exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.night.difficulty@v1',          label: 'Difficulty',               scope: 'night',      exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.night.playerCount@v1',         label: 'Player count',             scope: 'night',      exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.night.bossesKilled@v1',        label: 'Bosses killed',            scope: 'night',      exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.night.bossesInProgress@v1',    label: 'Bosses in progress',       scope: 'night',      exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.encounter.killSummary@v1',     label: 'Kill details',             scope: 'killed',     exact: false, confidence: 1.0, derivedFrom: 'raid.pull.durationMs@v1, raid.pull.deaths@v1', source: 'BoxScore' },
  { id: 'raid.pull.deaths@v1',               label: 'Deaths per pull',          scope: 'inprogress', exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.encounter.deathTrend@v1',      label: 'Death trend',              scope: 'inprogress', exact: false, derivedFrom: 'raid.pull.deaths@v1', source: 'BoxScore' },
  { id: 'raid.encounter.bestProgressPct@v1', label: 'Best progress',            scope: 'inprogress', exact: false, confidence: 1.0, derivedFrom: 'raid.pull.bossEndPctHp@v1', source: 'BoxScore' },
  { id: 'raid.encounter.fastWipePulls@v1',   label: 'Fast wipes (<=30s)',       scope: 'inprogress', exact: true, confidence: 1.0, source: 'BoxScore' },
  { id: 'raid.encounter.longestPulls@v1',    label: 'Longest attempts',         scope: 'inprogress', exact: true, confidence: 1.0, source: 'BoxScore' },
]

// Default: everything except the date (the night is already the user's selection).
export const NIGHT_DEFAULT_IDS = new Set(
  NIGHT_PALETTE.map(p => p.id).filter(id => id !== 'raid.night.date@v1')
)

function fmtDur(ms) {
  const t = Math.max(0, Math.round(ms / 1000))
  return `${String(Math.floor(t / 60)).padStart(2, '0')}:${String(t % 60).padStart(2, '0')}`
}

// Night-scoped raw value (also the display value — these are short scalars). Null = skip.
function nightValue(night, id) {
  switch (id) {
    case 'raid.night.zone@v1':             return night.zone || null
    case 'raid.night.date@v1':             return night.date || null
    case 'raid.night.difficulty@v1':       return night.difficulty || null
    case 'raid.night.playerCount@v1':      return night.playerCount > 0 ? night.playerCount : null
    case 'raid.night.bossesKilled@v1':     return night.bossesKilled
    case 'raid.night.bossesInProgress@v1': return night.bossesInProgress
    default:                                return null
  }
}

// Encounter-scoped raw value, mirroring the markdown box score's lines. Null = skip
// (the markdown omits the same lines); best-progress states its absence explicitly
// instead, exactly like the markdown does.
function encounterValue(enc, id) {
  switch (id) {
    case 'raid.encounter.killSummary@v1':
      return enc.killDurationMs != null ? `killed in ${fmtDur(enc.killDurationMs)}, ${enc.killDeaths} deaths` : null
    case 'raid.pull.deaths@v1':
      return enc.deathsPerPull?.length ? `${enc.deathsPerPull.join(', ')} (pulls 1..${enc.deathsPerPull.length})` : null
    case 'raid.encounter.deathTrend@v1':
      return enc.deathTrend || null
    case 'raid.encounter.bestProgressPct@v1':
      if (enc.executePulls?.length) return `reached boss 0% (execute range) on pulls ${enc.executePulls.join(', ')}`
      if (enc.bestProgressPct != null) return `lowest boss HP reached was ${Math.round(enc.bestProgressPct)}%`
      return 'no reliable boss-HP reading was recorded'
    case 'raid.encounter.fastWipePulls@v1':
      return enc.fastWipePulls?.length ? `pulls ${enc.fastWipePulls.join(', ')}` : null
    case 'raid.encounter.longestPulls@v1':
      return enc.longestPulls?.length ? enc.longestPulls.map(p => `#${p.n} (${fmtDur(p.durationMs)})`).join(', ') : null
    default:
      return null
  }
}

function nightConfidence(prop, enc) {
  if (prop.id === 'raid.encounter.deathTrend@v1') return Math.min((enc?.pullCount || 0) / 10, 0.95)
  return prop.confidence ?? 1.0
}

function propertyLine(prop, value, conf, indent) {
  const provenanceAttr = prop.exact
    ? 'exact="true"'
    : `derived="true" derivedFrom="${esc(prop.derivedFrom)}"`
  return `${indent}<property id="${esc(prop.id)}" ${provenanceAttr} confidence="${conf.toFixed(2)}" source="${esc(prop.source)}">${esc(String(value))}</property>`
}

// Build the versioned XML context + display list for the night lens.
// night = the .night.v1.json artifact; selectedIds = Set<string> from NIGHT_PALETTE.
export function buildNightLens(night, selectedIds) {
  const sorted = [...selectedIds].sort((a, b) => a.localeCompare(b, 'en', { sensitivity: 'variant' }))
  const allIds = NIGHT_PALETTE.map(p => p.id)
  const omittedIds = allIds.filter(id => !selectedIds.has(id))
  const selectedLabels = sorted.map(id => NIGHT_PALETTE.find(p => p.id === id)?.label).filter(Boolean).join(', ')
  const omittedLabels = omittedIds.map(id => NIGHT_PALETTE.find(p => p.id === id)?.label).filter(Boolean).join(', ')

  const displayItems = []
  const nightLines = []
  for (const id of sorted) {
    const prop = NIGHT_PALETTE.find(p => p.id === id)
    if (!prop || prop.scope !== 'night') continue
    const value = nightValue(night, id)
    if (value === null || value === undefined) continue
    nightLines.push(propertyLine(prop, value, nightConfidence(prop, null), '    '))
    displayItems.push({ id, label: prop.label, valueLabel: String(value) })
  }

  const encBlocks = []
  for (const enc of night.encounters || []) {
    const wantScope = enc.killed ? 'killed' : 'inprogress'
    const lines = []
    for (const id of sorted) {
      const prop = NIGHT_PALETTE.find(p => p.id === id)
      if (!prop || prop.scope !== wantScope) continue
      const value = encounterValue(enc, id)
      if (value === null || value === undefined) continue
      lines.push(propertyLine(prop, value, nightConfidence(prop, enc), '      '))
      displayItems.push({
        id: `${id}:${enc.name}`,
        label: `${enc.name} — ${prop.label}`,
        valueLabel: String(value),
      })
    }
    if (lines.length > 0)
      encBlocks.push([
        `    <encounter name="${esc(enc.name)}" killed="${enc.killed ? 'yes' : 'no'}" pulls="${enc.pullCount}">`,
        ...lines,
        `    </encounter>`,
      ].join('\n'))
  }

  const inner = [
    `  <night>`,
    `    <UserIntent>`,
    `      <selected>${esc(selectedLabels)}</selected>`,
    `      <explicitlyOmitted>${esc(omittedLabels || 'none')}</explicitlyOmitted>`,
    `    </UserIntent>`,
    ...nightLines,
    ...encBlocks,
    `  </night>`,
  ].join('\n')

  const digest = sha256hex(inner)
  const xml = `<context version="1" digest="sha256:${digest}" propertyCount="${displayItems.length}">\n${inner}\n</context>`

  return { xml, displayItems, propertyCount: displayItems.length }
}
