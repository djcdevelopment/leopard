// Career lens for the query builder / lens composer.
// Pure logic — no React. Takes a structured roster boss row and a Set of selected property IDs
// and produces { xml, displayItems, propertyCount }.
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
