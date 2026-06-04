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
