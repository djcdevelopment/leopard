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
