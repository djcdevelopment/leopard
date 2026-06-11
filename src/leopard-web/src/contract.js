// Slice-contract compiler for the Explorer: turns the user's selected knowledge objects
// (the "contract") into the versioned XML context the model receives. Pure logic, no React.
// Sibling of lens.js (which stays untouched): same esc() rules, same sort-by-id
// canonicalization, same digest-over-inner-XML stability rule, sha256hex shared from
// context.js. The Explorer feeds the returned xml through ContextBuilder.setText() so the
// display==send invariant stays structural.

import { sha256hex } from './context.js'
import { getObject } from './knowledge.js'

export function esc(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

// The documented chars/4 heuristic — drives tok= attributes and the status line. An
// estimate by design: the real tokenizer lives in the provider, and the contract only
// needs relative weight, not billing accuracy.
export function estimateTokens(text) {
  return Math.ceil(String(text).length / 4)
}

// ── per-module payload serializers ──────────────────────────────────────────
// Each returns deterministic plain text (the slice body). Missing data serializes as an
// explicit absence line — the model is told what is NOT known, never handed silence.

const MAX_SERIES_SAMPLES = 48
const DEATH_WINDOW_SEC = 10

function fmt(v, digits = 2) {
  if (v === null || v === undefined || Number.isNaN(v)) return '-'
  const n = Number(v)
  return Number.isInteger(n) ? String(n) : n.toFixed(digits).replace(/0+$/, '').replace(/\.$/, '')
}

function downsample(values, keep) {
  if (!values || values.length === 0) return []
  if (values.length <= MAX_SERIES_SAMPLES) return values
  const stride = Math.ceil(values.length / MAX_SERIES_SAMPLES)
  const out = []
  for (let i = 0; i < values.length; i += stride) out.push(values[i])
  return out
}

function findPull(artifact, pullId) {
  for (const enc of artifact?.encounters || []) {
    const hit = (enc.pulls || []).find((p) => p.pullId === pullId)
    if (hit) return { enc, pull: hit }
  }
  return null
}

// Shared lookup for the UI (Properties preview, Tab orchestration): one pull's entry in a
// per-night artifact ({ pullId, n, outcome, signals|scores }).
export function findPullIn(artifact, pullId) {
  return findPull(artifact, pullId)?.pull || null
}

function serializeSignals(nightData, pullId, cfg) {
  const hit = findPull(nightData.signals, pullId)
  const s = hit?.pull?.signals
  if (!s) return '(no replay-derived signals for this pull - advanced combat logging may have been off)'
  const a = s.aggregates
  const lines = []
  lines.push(`aggregates: coverage avg ${fmt(a.coverageAvg * 100, 0)}% / min ${fmt(a.coverageMin * 100, 0)}% - fragile ${fmt(a.fragileSec, 0)} s - snaps ${a.snapCount} - tightest spacing ${fmt(a.spacingTightest, 1)} yd - deaths ${a.deathsTotal} - duration ${a.durationSec} s`)
  for (const snap of s.snaps || []) lines.push(`snap: coverage -${snap.dropPct}pp at ${snap.atSec}s`)

  if (cfg.agg === 'none' && cfg.rep === 'timeline') {
    // Optional time bound: keep only seconds within ±10 s of a death.
    let mask = null
    if (cfg.time === 'around deaths') {
      const deaths = s.signals?.deathsPerSec?.values || []
      mask = new Array(deaths.length).fill(false)
      deaths.forEach((v, sec) => {
        if (!v) return
        for (let k = Math.max(0, sec - DEATH_WINDOW_SEC); k <= Math.min(deaths.length - 1, sec + DEATH_WINDOW_SEC); k++) mask[k] = true
      })
      lines.push(`time bound: +/-${DEATH_WINDOW_SEC} s around each death (${mask.filter(Boolean).length} of ${deaths.length} s kept)`)
    }
    const order = ['spacing', 'coverage', 'deathsPerSec', 'followership', 'entropy', 'hpVariance']
    for (const key of order) {
      const series = s.signals?.[key]
      if (!series) continue
      const values = mask ? series.values.filter((_, i) => mask[i]) : series.values
      const samples = downsample(values).map((v) => fmt(v))
      const unit = series.unit ? ` (${series.unit})` : ''
      lines.push(`${key}${unit}: ${samples.join(' ')} [peak ${fmt(series.peak?.value)} @ ${series.peak?.atSec}s]`)
    }
  }
  return lines.join('\n')
}

function serializePlayers(nightData, pullId, cfg) {
  const hit = findPull(nightData.players, pullId)
  const scores = hit?.pull?.scores
  if (!scores || scores.length === 0) return '(no player scores for this pull - needs replay frames)'
  const rows = cfg.scope === 'leaders' ? scores.slice(0, 3) : scores
  const lines = rows.map((p, i) =>
    `${i + 1}. ${p.displayName} (${p.role || 'Unknown'}): composite ${p.compositeScore} - move ${p.movementScore} dmg ${p.damageScore} surv ${p.survivalScore} aware ${p.awarenessScore} - ${p.archetype}`)
  if (cfg.scope === 'leaders' && scores.length > 3) lines.push(`(${scores.length - 3} more players omitted by scope=leaders)`)
  lines.push('(scores rank players within this pull; damage is a lower bound - replays retain top-decile damage events)')
  return lines.join('\n')
}

function serializeAffinity(nightData, cfg) {
  const a = nightData.affinity
  if (!a || !(a.participants || []).length) return '(no movement-affinity structure for this night - needs replay frames)'
  const lines = []
  lines.push(`participants: ${a.participants.length} players tracked across pulls`)
  for (const g of a.groups || []) lines.push(`group ${g.groupId} (${g.members.length}): ${g.members.join(', ')}`)
  const gaps = a.coverageGaps
  if (gaps) {
    if ((gaps.gaps || []).length === 0) lines.push('coverage gaps: none detected')
    for (const gap of gaps.gaps || []) lines.push(`gap [${gap.kind}]: ${gap.interpretation}`)
    if (gaps.tanksWellPaired) lines.push('tanks co-travel above the swap threshold (well paired)')
  }
  if (cfg.rep === 'matrix') {
    const ids = a.participants.map((p) => p.id)
    const names = new Map(a.participants.map((p) => [p.id, p.name]))
    const n = ids.length
    const pairs = []
    for (let i = 0; i < n; i++)
      for (let j = i + 1; j < n; j++)
        pairs.push({ a: names.get(ids[i]), b: names.get(ids[j]), c: a.composite[i * n + j] })
    if (pairs.length) {
      pairs.sort((x, y) => y.c - x.c || `${x.a}|${x.b}`.localeCompare(`${y.a}|${y.b}`, 'en'))
      const mean = pairs.reduce((s, p) => s + p.c, 0) / pairs.length
      lines.push(`pair composite: mean ${fmt(mean)} across ${pairs.length} pairs`)
      lines.push(`tightest pairs: ${pairs.slice(0, 3).map((p) => `${p.a}+${p.b} ${fmt(p.c)}`).join(', ')}`)
      lines.push(`loosest pairs: ${pairs.slice(-3).map((p) => `${p.a}+${p.b} ${fmt(p.c)}`).join(', ')}`)
    }
  }
  lines.push('(affinity = fraction of shared frames within 10 yd blended with co-direction; whole night, all pulls with frames)')
  return lines.join('\n')
}

function serializeDiff(nightData, pullId, extra) {
  const d = nightData.diff
  if (!extra?.comparePullId) return '(no compare pull selected - pick one in Properties)'
  if (!d) return '(diff not loaded for the selected pulls)'
  const lines = []
  lines.push(`${d.encounterName} (${d.encounterDifficulty}): pull #${d.left?.n} vs pull #${d.right?.n}`)
  lines.push(d.ruleHeadline)
  for (const m of d.metrics || []) {
    if (!m.wired) { lines.push(`${m.label}: (requires replay frames - ${m.detail || 'not available'})`); continue }
    const delta = m.delta ? ` (${m.delta}, ${m.dir})` : m.dir !== 'flat' ? ` (${m.dir})` : ''
    lines.push(`${m.label}: ${fmt(m.l)}${m.unit} -> ${fmt(m.r)}${m.unit}${delta}${m.detail ? ` - ${m.detail}` : ''}`)
  }
  return lines.join('\n')
}

// One slice's body text, given its registry entry + slice config + fetched night data.
export function serializeSlice(entry, cfg, nightData, pullRef) {
  switch (entry.api) {
    case 'signals': return serializeSignals(nightData, pullRef.pullId, cfg)
    case 'players': return serializePlayers(nightData, pullRef.pullId, cfg)
    case 'affinity': return serializeAffinity(nightData, cfg)
    case 'diff': return serializeDiff(nightData, pullRef.pullId, pullRef.extra)
    default: return '(this knowledge object is not wired yet)'
  }
}

// Simple coverage label for the status line: how many distinct categories the contract spans.
export function coverageLabel(slices) {
  const cats = new Set(slices.map((s) => getObject(s.objectId)?.category).filter(Boolean))
  if (cats.size === 0) return 'Empty contract'
  if (cats.size === 1) return 'Narrow coverage'
  if (cats.size === 2) return 'Mixed coverage'
  return 'Balanced coverage'
}

// Build the full contract.
//   pull:          { pullId, n, outcome }
//   encounterName, difficulty, durationMs: the selected pull's frame
//   slices:        [{ objectId, slice: {scope,rep,agg,time}, extra: {comparePullId} }]
//   nightData:     { signals, players, affinity, diff } (fetched artifacts; any may be null)
// Returns { xml, sliceItems, totalTok, digest, coverage }.
export function buildContract({ pull, encounterName, difficulty, durationMs, slices, nightData }) {
  // Canonical serialization rule #1 (lens.js): sort by stable ID, ordinal.
  const sorted = [...slices].sort((a, b) => a.objectId.localeCompare(b.objectId, 'en', { sensitivity: 'variant' }))

  const sliceItems = []
  const sliceBlocks = []
  for (const s of sorted) {
    const entry = getObject(s.objectId)
    if (!entry) continue
    const cfg = { ...(entry.sliceDefaults || {}), ...(s.slice || {}) }
    const body = serializeSlice(entry, cfg, nightData, { pullId: pull.pullId, extra: s.extra })
    const tok = estimateTokens(body)
    const attrs = [
      `object="${esc(s.objectId)}"`,
      `scope="${esc(cfg.scope || 'raid')}"`,
      `rep="${esc(cfg.rep || 'snapshot')}"`,
      `agg="${esc(cfg.agg || 'none')}"`,
      `time="${esc(cfg.time || 'whole pull')}"`,
      `tok="${tok}"`,
    ].join(' ')
    sliceBlocks.push(`    <slice ${attrs}>\n${esc(body)}\n    </slice>`)
    sliceItems.push({ id: s.objectId, label: entry.label, cfg, tok, category: entry.category })
  }

  const inner = [
    `  <pull id="${esc(pull.pullId)}" n="${esc(pull.n)}" encounter="${esc(encounterName)}" difficulty="${esc(difficulty)}" outcome="${esc(pull.outcome || '')}" durationMs="${esc(durationMs ?? '')}">`,
    ...sliceBlocks,
    `  </pull>`,
  ].join('\n')

  // Digest over the inner XML, root excluded — the lens.js stability rule (the digest
  // attribute can't change the bytes it signs).
  const digest = sha256hex(inner)
  const xml = `<context version="1" digest="sha256:${digest}" slices="${sliceItems.length}">\n${inner}\n</context>`

  return { xml, sliceItems, totalTok: estimateTokens(xml), digest, coverage: coverageLabel(sorted) }
}
