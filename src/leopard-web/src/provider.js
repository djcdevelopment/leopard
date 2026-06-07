// Leopard provider layer — see docs/provider-contract.md.
// Leopard consumes a PROVIDER contract, not a hardware/runtime contract.
// Ollama is the default provider, reached via the Vite dev proxy (/ollama -> :11434).

const BASE = '/ollama'

// Detect the default provider and enumerate its installed models.
export async function detectProvider() {
  try {
    const res = await fetch(`${BASE}/api/tags`, { method: 'GET' })
    if (!res.ok) return { reachable: false, models: [], error: `HTTP ${res.status}` }
    const data = await res.json()
    const models = (data.models || []).map((m) => m.name)
    return { reachable: true, models }
  } catch (e) {
    return { reachable: false, models: [], error: String(e?.message || e) }
  }
}

// Models currently resident in the provider (Ollama /api/ps). Used to default the Leopard
// model picker to whatever is already warm — so the first Ask doesn't trigger a cold load
// (e.g. the *-b70 model the local-inference runbook warms onto the second card).
export async function loadedModels() {
  try {
    const res = await fetch(`${BASE}/api/ps`, { method: 'GET' })
    if (!res.ok) return []
    const data = await res.json()
    return (data.models || []).map((m) => m.name)
  } catch {
    return []
  }
}

// Stream a chat completion. Calls onToken(text) for each delta; resolves to the
// full text. Ollama streams newline-delimited JSON, each line { message:{content}, done }.
export async function chatStream({ model, messages, onToken, signal, temperature = 0.2 }) {
  const res = await fetch(`${BASE}/api/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ model, messages, stream: true, options: { temperature } }),
    signal,
  })
  if (!res.ok || !res.body) throw new Error(`chat failed: HTTP ${res.status}`)

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buf = ''
  let full = ''
  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buf += decoder.decode(value, { stream: true })
    let nl
    while ((nl = buf.indexOf('\n')) >= 0) {
      const line = buf.slice(0, nl).trim()
      buf = buf.slice(nl + 1)
      if (!line) continue
      try {
        const obj = JSON.parse(line)
        const tok = obj?.message?.content || ''
        if (tok) {
          full += tok
          onToken?.(tok)
        }
      } catch {
        /* ignore a partial / non-JSON line */
      }
    }
  }
  return full
}
