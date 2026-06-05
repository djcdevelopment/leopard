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

export async function setConfig(logDir) {
  const r = await fetch(`${A}/config`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ logDir }),
  })
  if (!r.ok) throw new Error(`config save HTTP ${r.status}`)
  return r.json()
}

// Native folder dialog — only works in the desktop shell (returns { available:false } in a browser).
export async function pickFolder() {
  const r = await fetch(`${A}/pick-folder`, { method: 'POST' })
  if (!r.ok) throw new Error(`pick-folder HTTP ${r.status}`)
  return r.json()
}
