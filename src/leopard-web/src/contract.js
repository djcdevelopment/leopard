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
  // Strip trailing zeros only after a decimal point — a bare /0+$/ would turn a non-integer
  // that ROUNDS to a trailing zero into garbage ("39.6" at digits=0 → "40" → "4").
  return Number.isInteger(n) ? String(n) : n.toFixed(digits).replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '')
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

function serializeCoverage(nightData, pullId, cfg) {
  const hit = findPull(nightData.coverage, pullId)
  const c = hit?.pull?.coverage
  if (!c) return '(no coverage quality model for this pull - needs replay frames)'
  const s = c.summary
  const lines = []
  lines.push(`summary: raid coverage avg ${fmt(s.avgRaidPct, 0)}% / min ${fmt(s.minRaidPct, 0)}% - tank avg ${fmt(s.avgTankPct, 0)}% / min ${fmt(s.minTankPct, 0)}% - quality avg ${fmt(s.avgQualityScore, 0)} / min ${fmt(s.minQualityScore, 0)} - fragile ${fmt(s.timeInFragileCoverageMs / 1000, 0)} s`)
  for (const snap of s.snappingPoints || []) {
    const dmg = snap.followedByDamageMs != null ? `, damage followed ${fmt(snap.followedByDamageMs / 1000, 1)} s later` : ', no damage followed'
    lines.push(`snap: quality -${fmt(snap.qualityDrop, 0)} at ${fmt(snap.timeMs / 1000, 0)}s${dmg}`)
  }
  if ((s.snappingPoints || []).length === 0) lines.push('snaps: none detected')
  if (cfg.rep === 'timeline') {
    const sec = c.seconds || {}
    for (const [key, label] of [['quality', 'quality score'], ['raidPct', 'raid coverage %']]) {
      const samples = downsample(sec[key] || []).map((v) => fmt(v, 0))
      if (samples.length) lines.push(`${label}: ${samples.join(' ')}`)
    }
  }
  lines.push('(quality = 100 x (0.5 healer-centrality + 0.5 (1 - edge-proximity)); fragile = raid coverage below 70%)')
  return lines.join('\n')
}

function serializeSegments(nightData, pullId) {
  const hit = findPull(nightData.segments, pullId)
  const p = hit?.pull
  if (!p || !p.segments) return '(no formation segments for this pull - needs replay frames)'
  if (p.segments.length === 0) return '(no stable formation periods detected - the pull may be too short)'
  const lines = []
  if (p.phases) lines.push(`phases: ${p.phases}`)
  for (const s of p.segments)
    lines.push(`${s.formation}: ${fmt(s.startMs / 1000, 0)}-${fmt(s.endMs / 1000, 0)}s - median pairwise ${fmt(s.medianPairwiseDistanceYd, 1)} yd (${s.frameCount} frames)`)
  lines.push('(stacked < 5 yd median pairwise, split 5-15 yd, dispersed >= 15 yd; boundaries from movement change-points)')
  return lines.join('\n')
}

function serializeClassify(nightData, pullId, cfg) {
  const hit = findPull(nightData.classify, pullId)
  const p = hit?.pull
  if (!p) return '(no wipe classification for this pull - re-parse in Setup)'
  const cls = p.classification
  if (!cls) return `(not classified - ${p.reason || 'no verdict'})`
  if (cls.kind === 'called-wipe')
    return `CALLED WIPE (${cls.calledWipePattern}) - the raid reset on purpose; do not analyze this wipe as a failure.`
  const lines = []
  lines.push(`verdict: ${cls.kind} collapse, confidence ${cls.confidence}, onset ~${fmt(cls.inflectionMs / 1000, 0)}s`)
  if ((cls.affected || []).length > 0 && cls.kind !== 'systemic')
    lines.push(`most implicated: ${cls.affected.join(', ')}`)
  if (cfg.rep !== 'verdict') {
    for (const e of cls.evidence || []) lines.push(`evidence [${e.signalId}]: ${e.reason} (value ${fmt(e.value)})`)
    if (cls.coveragePattern) lines.push(`coverage pattern: ${cls.coveragePattern}${cls.offender ? ` - ${cls.offender.displayName} at ${fmt(cls.offender.atMs / 1000, 0)}s` : ''}`)
  }
  lines.push('(deterministic rule tree - outcome/duration/death gates, consensus inflection, z-score attribution; no probabilities)')
  return lines.join('\n')
}

function serializeMeters(nightData, cfg) {
  const meters = nightData.affinity?.meters
  if (!meters || meters.length === 0) return '(no movement meters for this night - needs replay frames)'
  const ranked = [...meters].sort((a, b) => b.totalDistanceYd - a.totalDistanceYd)
  const rows = cfg.scope === 'top 5' ? ranked.slice(0, 5) : ranked
  const lines = rows.map((m, i) =>
    `${i + 1}. ${m.displayName} (${m.role || 'Unknown'}): total ${fmt(m.totalDistanceYd, 0)} yd - avg ${fmt(m.avgSpeedYdPerSec, 1)} yd/s - peak ${fmt(m.peakSpeedYdPerSec, 1)} yd/s - stationary ${fmt(m.stationaryRatio * 100, 0)}%`)
  if (cfg.scope === 'top 5' && ranked.length > 5) lines.push(`(${ranked.length - 5} more players omitted by scope=top 5)`)
  if (cfg.agg === 'wipes vs kills') {
    const byOutcome = nightData.affinity?.metersByOutcome
    if (!byOutcome || byOutcome.killSamples === 0 || byOutcome.wipeSamples === 0) {
      lines.push(`wipes vs kills: no contrast this night (${byOutcome ? `${byOutcome.wipeSamples} wipe / ${byOutcome.killSamples} kill pulls` : 'partition unavailable'})`)
    } else {
      lines.push(`wipes vs kills (${byOutcome.wipeSamples} wipe / ${byOutcome.killSamples} kill pulls), per-player wipe-minus-kill:`)
      for (const d of byOutcome.delta || []) {
        if (d.distanceDelta == null) continue
        lines.push(`${d.displayName}: distance ${d.distanceDelta > 0 ? '+' : ''}${fmt(d.distanceDelta, 0)} yd - avg speed ${d.avgSpeedDelta > 0 ? '+' : ''}${fmt(d.avgSpeedDelta, 2)} yd/s - stationary ${d.stationaryDelta > 0 ? '+' : ''}${fmt(d.stationaryDelta * 100, 0)}pp`)
      }
    }
  }
  lines.push('(whole-night meters across every pull with frames; peak speed flags blinks/teleports too)')
  return lines.join('\n')
}

function serializeShape(nightData, pullId) {
  const hit = findPull(nightData.shape, pullId)
  const d = hit?.pull?.density
  if (!d || !(d.cells || []).length) return '(no density grid for this pull - needs replay frames)'
  const { gridW, gridH, cells, arenaW, arenaH, totalSamples } = d
  const ranked = cells.map((v, i) => ({ v, i })).filter((c) => c.v > 0).sort((a, b) => b.v - a.v)
  if (ranked.length === 0 || !totalSamples) return '(density grid is empty for this pull)'
  const mass = ranked.reduce((s, c) => s + c.v, 0)
  let acc = 0
  let half = 0
  for (const c of ranked) { acc += c.v; half++; if (acc >= mass / 2) break }
  const top = ranked[0]
  const cx = ((top.i % gridW) + 0.5) / gridW * arenaW
  const cy = (Math.floor(top.i / gridW) + 0.5) / gridH * arenaH
  const lines = []
  lines.push(`arena ~${fmt(arenaW, 0)}x${fmt(arenaH, 0)} yd, ${gridW}x${gridH} occupancy grid, ${totalSamples} position samples`)
  lines.push(`hotspot: cell centered (${fmt(cx, 0)}, ${fmt(cy, 0)}) yd held the most raid-presence`)
  lines.push(`concentration: half of all standing time in ${half} of ${ranked.length} occupied cells (${fmt(half / ranked.length * 100, 0)}%)`)
  lines.push('(the long-exposure of the pull - where the raid actually stood, not where it should have)')
  return lines.join('\n')
}

// One slice's body text, given its registry entry + slice config + fetched night data.
export function serializeSlice(entry, cfg, nightData, pullRef) {
  switch (entry.api) {
    case 'signals': return serializeSignals(nightData, pullRef.pullId, cfg)
    case 'players': return serializePlayers(nightData, pullRef.pullId, cfg)
    case 'affinity': return serializeAffinity(nightData, cfg)
    case 'diff': return serializeDiff(nightData, pullRef.pullId, pullRef.extra)
    case 'coverage': return serializeCoverage(nightData, pullRef.pullId, cfg)
    case 'segments': return serializeSegments(nightData, pullRef.pullId)
    case 'classify': return serializeClassify(nightData, pullRef.pullId, cfg)
    case 'meters': return serializeMeters(nightData, cfg)
    case 'shape': return serializeShape(nightData, pullRef.pullId)
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
