#!/usr/bin/env node
// Leopard box-score generator.
//
// Runs Tempo's one-shot parser (Tempo.Diagnostics --summarize) and distills its
// session/encounter/pull readout into a compact, PRE-COMPUTED box score — trends
// and all. The point (Leopard's grounding bar): the local model should RESTATE
// facts, never INFER them. So we compute kills, death trends, best progress, and
// notable pulls here, deterministically, and hand the model only those facts.
//
// Parsing is token-based (pull#, Kill/Wipe, deaths=, boss N%, digits) so combat-log
// separator glyphs / console encoding can't break it.
//
// Usage: node make-boxscore.mjs <logPath> <outPath> [exePath]

import { execFileSync } from 'node:child_process'
import { writeFileSync } from 'node:fs'

const [, , logPath, outPath, exeArg] = process.argv
if (!logPath || !outPath) {
  console.error('usage: make-boxscore.mjs <logPath> <outPath> [exePath]')
  process.exit(2)
}
const exe = exeArg || 'D:/World of Warcraft/tempo/src/Tempo.Diagnostics/bin/Debug/net9.0/Tempo.Diagnostics.exe'

// .NET writes redirected stdout as UTF-8; decode it as such so separators (·, —) are clean.
// Parsing is still token-based, so even a wrong glyph wouldn't break the numbers.
const raw = execFileSync(exe, ['--log', logPath, '--summarize'], { encoding: 'utf8', maxBuffer: 128 * 1024 * 1024 })
const lines = raw.split(/\r?\n/)

const DIFF = /\b(Heroic|Mythic|Normal|LFR|Unknown)\b/
const avg = (a) => (a.length ? a.reduce((s, x) => s + x, 0) / a.length : 0)

let zone = '', dateStr = '', participants = 0
const encounters = []
let cur = null

for (const ln of lines) {
  if (/^==\s*session/.test(ln)) {
    const z = ln.match(/·\s*([^·]+?)\s*·\s*started/) // best-effort zone
    if (z) zone = z[1].trim()
    const d = ln.match(/started\s+(\d{4}-\d{2}-\d{2})/)
    if (d) dateStr = d[1]
    continue
  }
  const pm = ln.match(/participants:\s*(\d+)/)
  if (pm) { participants = +pm[1]; continue }
  if (/^\s*##/.test(ln)) {
    const cntM = ln.match(/(\d+)\s*pull/)
    const diff = (ln.match(DIFF) || [])[1] || 'Unknown'
    let name = ln.replace(/^\s*##\s*/, '')
    const di = name.search(DIFF)
    if (di > 0) name = name.slice(0, di)
    name = name.replace(/[\s·—\-|]+$/, '').trim()
    cur = { name, diff, pulls: [], declared: cntM ? +cntM[1] : 0 }
    encounters.push(cur)
    continue
  }
  const plm = ln.match(/pull#(\d+)/)
  if (plm && cur) {
    const outcome = (ln.match(/\b(Kill|Wipe)\b/) || [])[1] || '?'
    const dur = ln.match(/(\d+):(\d+)/)
    const durSec = dur ? +dur[1] * 60 + +dur[2] : 0
    const durStr = dur ? `${dur[1]}:${dur[2]}` : '?'
    const deaths = +((ln.match(/deaths=(\d+)/) || [])[1] || 0)
    const hpM = ln.match(/boss\s+(\d+)%/)
    cur.pulls.push({ num: +plm[1], outcome, durSec, durStr, deaths, bossPct: hpM ? +hpM[1] : null })
  }
}

const killed = encounters.filter((e) => e.pulls.some((p) => p.outcome === 'Kill'))
const inProgress = encounters.filter((e) => e.pulls.length && e.pulls.every((p) => p.outcome !== 'Kill'))

function deathTrend(deaths) {
  if (deaths.length < 4) return `${deaths.length} attempt(s) — too few to call a trend`
  const t = Math.max(1, Math.floor(deaths.length / 3))
  const early = avg(deaths.slice(0, t))
  const late = avg(deaths.slice(-t))
  const peak = Math.max(...deaths)
  if (late - early > 2) return `deaths rose then held (early ~${Math.round(early)} -> later ~${Math.round(late)} per pull; peak ${peak}; did NOT decrease)`
  if (early - late > 2) return `deaths fell over the night (early ~${Math.round(early)} -> later ~${Math.round(late)} per pull)`
  return `deaths held roughly steady (~${Math.round(avg(deaths))} per pull; peak ${peak})`
}

const diffLabel = (killed[0]?.diff) || (inProgress[0]?.diff) || ''
let md = `# ${zone || 'This raid night'}${diffLabel ? ` (${diffLabel})` : ''}${dateStr ? ` - ${dateStr}` : ''}${participants ? ` - ${participants} players` : ''}\n`
md += `RESULT: ${killed.length} bosses KILLED, ${inProgress.length} boss(es) IN PROGRESS.\n\n`

if (killed.length) {
  md += `## Bosses killed (${killed.length})\n`
  for (const e of killed) {
    const kp = e.pulls.find((p) => p.outcome === 'Kill')
    md += `- ${e.name} - killed in ${kp.durStr}, ${kp.deaths} deaths\n`
  }
  md += `\n`
}

for (const e of inProgress) {
  const deaths = e.pulls.map((p) => p.deaths)
  md += `## In progress: ${e.name} - ${e.pulls.length} wipes\n`
  md += `- Deaths per pull (1..${e.pulls.length}): ${deaths.join(',')}\n`
  md += `- Death trend: ${deathTrend(deaths)}\n`
  const executed = e.pulls.filter((p) => p.bossPct === 0).map((p) => p.num)
  const withHp = e.pulls.filter((p) => p.bossPct !== null && p.bossPct <= 100)
  if (executed.length) md += `- Best progress: reached boss 0% (execute range) on pulls ${executed.join(', ')}\n`
  else if (withHp.length) md += `- Best progress: lowest boss HP reached was ${Math.min(...withHp.map((p) => p.bossPct))}%\n`
  else md += `- Best progress: no reliable boss-HP reading was recorded\n`
  const fast = e.pulls.filter((p) => p.durSec > 0 && p.durSec <= 30).map((p) => p.num)
  if (fast.length) md += `- Very fast wipes (<=30s, died early): pulls ${fast.join(', ')}\n`
  const longest = e.pulls.slice().sort((a, b) => b.durSec - a.durSec).slice(0, 3).map((p) => `#${p.num} (${p.durStr})`)
  if (longest.length) md += `- Longest attempts: ${longest.join(', ')}\n`
  md += `\n`
}

md += `_(Box score computed by Leopard from Tempo's parser. Every figure above is exact — reflect on these facts, do not infer beyond them.)_\n`

writeFileSync(outPath, md, 'utf8')
console.log(`box score written: ${outPath} (${md.length} chars)`)
console.log('-----')
console.log(md)
