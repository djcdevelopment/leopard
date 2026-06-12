// Client for the Leopard .NET host (Kestrel on :5280, reached via the Vite /api proxy).
// This is the backend that lists logs, runs Tempo's parser, and serves box scores —
// all .NET, no Node for the user.
const A = '/api'

export async function getConfig() {
  const r = await fetch(`${A}/config`)
  if (!r.ok) throw new Error(`config HTTP ${r.status}`)
  return r.json()
}

export async function getLogs() {
  const r = await fetch(`${A}/logs`)
  if (!r.ok) throw new Error(`logs HTTP ${r.status}`)
  return r.json()
}

export async function parseLogs(names) {
  const r = await fetch(`${A}/parse`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ names }),
  })
  if (!r.ok) throw new Error(`parse HTTP ${r.status}`)
  return r.json()
}

export async function getBoxscore(name) {
  const r = await fetch(`${A}/boxscore?name=${encodeURIComponent(name)}`)
  if (!r.ok) throw new Error(`boxscore HTTP ${r.status}`)
  return r.text()
}

// Night — the box score's figures as structured JSON (the night-lens substrate). 404 =>
// parsed before the night artifact existed — Ask falls back to the markdown box score.
export async function getNight(name) {
  const r = await fetch(`${A}/night?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`night HTTP ${r.status}`)
  return r.json()
}

// Per-encounter Trends artifact (rule-row windows + coherence series) for a parsed night.
// 404 means the night was parsed before Trends existed — re-parse it in Setup.
export async function getTrends(name) {
  const r = await fetch(`${A}/trends?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`trends HTTP ${r.status}`)
  return r.json()
}

// Pipeline trace (per-stage substrate counts + samples + trim collapse) for the Explorer.
// 404 means the night was parsed before the trace existed — re-parse it in Setup.
export async function getTrace(name) {
  const r = await fetch(`${A}/trace?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`trace HTTP ${r.status}`)
  return r.json()
}

// Signals — the six-signal diagnostic pack per pull (spacing / coverage / deathsPerSec /
// followership / entropy / hpVariance + snaps + aggregates). 404 => parsed before Signals
// existed — re-parse in Setup.
export async function getSignals(name) {
  const r = await fetch(`${A}/signals?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`signals HTTP ${r.status}`)
  return r.json()
}

// Players — per-pull role-weighted scores + archetypes. 404 => re-parse in Setup.
export async function getPlayers(name) {
  const r = await fetch(`${A}/players?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`players HTTP ${r.status}`)
  return r.json()
}

// Affinity — the night's movement-group structure (matrix + groups + coverage gaps +
// embedded meters). Night-scoped, not per-pull. 404 => re-parse in Setup.
export async function getAffinity(name) {
  const r = await fetch(`${A}/affinity?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`affinity HTTP ${r.status}`)
  return r.json()
}

// Diff — deterministic two-pull comparison within one night (same boss). Needs the night
// name plus both pull ids. 404 => pull/night not found or not parsed.
export async function getDiff(name, a, b) {
  const r = await fetch(`${A}/diff?name=${encodeURIComponent(name)}&a=${encodeURIComponent(a)}&b=${encodeURIComponent(b)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`diff HTTP ${r.status}`)
  return r.json()
}

// Coverage — per-pull healing-coverage quality (per-second series + summary + snap markers).
// 404 => parsed before Coverage existed — re-parse in Setup.
export async function getCoverage(name) {
  const r = await fetch(`${A}/coverage?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`coverage HTTP ${r.status}`)
  return r.json()
}

// Segments — per-pull formation phases (stacked / split / dispersed change-points).
// 404 => parsed before Segments existed — re-parse in Setup.
export async function getSegments(name) {
  const r = await fetch(`${A}/segments?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`segments HTTP ${r.status}`)
  return r.json()
}

// Classify — per-pull wipe verdicts from the deterministic rule tree (kind + confidence +
// evidence + called-wipe gate). 404 => parsed before Classify existed — re-parse in Setup.
export async function getClassify(name) {
  const r = await fetch(`${A}/classify?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`classify HTTP ${r.status}`)
  return r.json()
}

// Career-arc grounding — one boss's all-time story as exact-figures text (the zoom above the
// per-night box score). Mirrors getBoxscore. 404 => no such career.
export async function getCareerSummary(careerId) {
  const r = await fetch(`${A}/career-summary?careerId=${encodeURIComponent(careerId)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`career-summary HTTP ${r.status}`)
  return r.text()
}

// Shape (density) — per-pull heatmaps for a parsed night. 404 => parsed before Shape existed
// (re-parse in Setup). Returns { encounters: [{ ..., pulls: [{ pullId, density, ... }] }] }.
export async function getShapeDensity(name) {
  const r = await fetch(`${A}/shape/density?name=${encodeURIComponent(name)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`shape density HTTP ${r.status}`)
  return r.json()
}

// Shape (kill-vs-wipe) — career-scoped contrast for a boss, fanned across every parsed night.
// Keyed by careerId (not a single night). 404 => career has no resolvable contrast.
export async function getShapeWkDelta(careerId) {
  const r = await fetch(`${A}/shape/wkdelta?careerId=${encodeURIComponent(careerId)}`)
  if (r.status === 404) return null
  if (!r.ok) throw new Error(`shape wkdelta HTTP ${r.status}`)
  return r.json()
}

// The Roster — all-time career per boss, fanned in across every parsed night. No name
// argument: it aggregates the whole corpus. Returns { bosses: [...] }.
export async function getCareer() {
  const r = await fetch(`${A}/career`)
  if (!r.ok) throw new Error(`career HTTP ${r.status}`)
  return r.json()
}

export async function setConfig(logDir) {
  const r = await fetch(`${A}/config`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ logDir }),
  })
  if (!r.ok) throw new Error(`config save HTTP ${r.status}`)
  return r.json()
}

// ── Live (between-pull insight) ── the host watches the live combat log via Tempo's ingest
// front and pre-generates one grounded insight per pull. See docs/live-insight-design-brief.md.

export async function getLiveStatus() {
  const r = await fetch(`${A}/live/status`)
  if (!r.ok) throw new Error(`live status HTTP ${r.status}`)
  return r.json()
}

export async function getLiveInsight() {
  const r = await fetch(`${A}/live/insight`)
  if (!r.ok) throw new Error(`live insight HTTP ${r.status}`)
  return r.json()
}

export async function postLiveFeedback(body) {
  const r = await fetch(`${A}/live/feedback`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!r.ok) throw new Error(`live feedback HTTP ${r.status}`)
  return r.json()
}

// Native folder dialog — only works in the desktop shell (returns { available:false } in a browser).
export async function pickFolder() {
  const r = await fetch(`${A}/pick-folder`, { method: 'POST' })
  if (!r.ok) throw new Error(`pick-folder HTTP ${r.status}`)
  return r.json()
}
