// Leopard provider layer — see docs/provider-contract.md.
// Leopard consumes a PROVIDER contract, not a hardware/runtime contract. A provider is a
// base URL plus the chat API it speaks: "ollama" (the default, /api/tags + /api/chat NDJSON)
// or "openai" (/v1/models + /v1/chat/completions SSE — llama-server, vllama, anything
// OpenAI-shaped). The host's /llm route forwards to the CONFIGURED provider URL
// (config.json: askProviderUrl / askProviderApi); this module reads the api kind from
// /api/config and speaks the matching protocol. Null config = local Ollama, byte-identical
// to the pre-config behavior.

const BASE = '/llm'

// The api kind the last detectProvider() resolved — chatStream/loadedModels speak this.
let apiKind = 'ollama'

async function configuredApi() {
  try {
    const res = await fetch('/api/config')
    if (!res.ok) return 'ollama'
    const cfg = await res.json()
    return normalizeApi(cfg.askProviderApi)
  } catch {
    return 'ollama'
  }
}

// ── pure helpers (vitest-covered) ───────────────────────────────────────────

export function normalizeApi(api) {
  return String(api || 'ollama').toLowerCase() === 'openai' ? 'openai' : 'ollama'
}

// Model enumeration response → model-name list, per api shape.
export function parseModels(api, data) {
  if (api === 'openai') return (data?.data || []).map((m) => m.id).filter(Boolean)
  return (data?.models || []).map((m) => m.name).filter(Boolean)
}

// One stream line → the token it carries (or null: keep-alive, done-marker, non-JSON).
// ollama: newline-delimited JSON, { message: { content }, done }.
// openai: SSE "data: {...}" lines with choices[0].delta.content, terminated by "data: [DONE]".
export function extractStreamToken(api, line) {
  const s = line.trim()
  if (!s) return null
  let payload = s
  if (api === 'openai') {
    if (!s.startsWith('data:')) return null
    payload = s.slice(5).trim()
    if (payload === '[DONE]') return null
  }
  try {
    const obj = JSON.parse(payload)
    if (api === 'openai') return obj?.choices?.[0]?.delta?.content || null
    return obj?.message?.content || null
  } catch {
    return null
  }
}

export function chatRequest(api, { model, messages, temperature }) {
  if (api === 'openai') {
    return {
      path: '/v1/chat/completions',
      body: { model, messages, stream: true, temperature },
    }
  }
  return {
    path: '/api/chat',
    body: { model, messages, stream: true, options: { temperature } },
  }
}

// ── the provider surface ────────────────────────────────────────────────────

// Detect the configured provider and enumerate its installed models.
export async function detectProvider() {
  apiKind = await configuredApi()
  const path = apiKind === 'openai' ? '/v1/models' : '/api/tags'
  try {
    const res = await fetch(`${BASE}${path}`, { method: 'GET' })
    if (!res.ok) return { reachable: false, models: [], error: `HTTP ${res.status}` }
    const data = await res.json()
    return { reachable: true, models: parseModels(apiKind, data) }
  } catch (e) {
    return { reachable: false, models: [], error: String(e?.message || e) }
  }
}

// Models currently resident in the provider (Ollama /api/ps). Used to default the Leopard
// model picker to whatever is already warm — so the first Ask doesn't trigger a cold load.
// OpenAI-shaped providers have no resident-model endpoint; whatever /v1/models lists is
// assumed servable (llama-server holds its model loaded by construction).
export async function loadedModels() {
  if (apiKind === 'openai') return []
  try {
    const res = await fetch(`${BASE}/api/ps`, { method: 'GET' })
    if (!res.ok) return []
    const data = await res.json()
    return (data.models || []).map((m) => m.name)
  } catch {
    return []
  }
}

// Stream a chat completion in whichever protocol the provider speaks. Calls onToken(text)
// for each delta; resolves to the full text. Both protocols arrive line-delimited, so one
// reader loop feeds extractStreamToken.
export async function chatStream({ model, messages, onToken, signal, temperature = 0.2 }) {
  const { path, body } = chatRequest(apiKind, { model, messages, temperature })
  const res = await fetch(`${BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
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
      const line = buf.slice(0, nl)
      buf = buf.slice(nl + 1)
      const tok = extractStreamToken(apiKind, line)
      if (tok) {
        full += tok
        onToken?.(tok)
      }
    }
  }
  return full
}
