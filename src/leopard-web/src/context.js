import React from 'react'

// CanonicalContext: the single object that owns the bytes the model receives AND what
// the user sees. render() is DEFINED IN TERMS OF serialize() so display==send is
// structural, not merely asserted. schemaVersion is in place for the XML era.
//
// Usage:
//   const ctx = new ContextBuilder().setText(markdownBlob, 'this raid night').freeze()
//   const ctx = new ContextBuilder().setTrend(enc).freeze()

export const SCHEMA_VERSION = '1'

// ---------------------------------------------------------------------------
// Synchronous SHA-256 (pure JS, no deps, browser + Node compatible)
// ---------------------------------------------------------------------------
function sha256hex(str) {
  const K = [
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
  ]
  const rotr = (x, n) => (x >>> n) | (x << (32 - n))
  const bytes = new TextEncoder().encode(str)
  const msgLen = bytes.length
  const bitLen = msgLen * 8
  const blockLen = (msgLen + 9 + 63) & ~63
  const padded = new Uint8Array(blockLen)
  padded.set(bytes)
  padded[msgLen] = 0x80
  const dv = new DataView(padded.buffer)
  dv.setUint32(blockLen - 4, bitLen >>> 0, false) // 64-bit length; high 32 bits stay 0

  let H0 = 0x6a09e667, H1 = 0xbb67ae85, H2 = 0x3c6ef372, H3 = 0xa54ff53a
  let H4 = 0x510e527f, H5 = 0x9b05688c, H6 = 0x1f83d9ab, H7 = 0x5be0cd19
  const w = new Array(64)

  for (let o = 0; o < blockLen; o += 64) {
    for (let i = 0; i < 16; i++) w[i] = dv.getUint32(o + i * 4, false)
    for (let i = 16; i < 64; i++) {
      const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3)
      const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10)
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0
    }
    let a = H0, b = H1, c = H2, d = H3, e = H4, f = H5, g = H6, h = H7
    for (let i = 0; i < 64; i++) {
      const S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25)
      const ch = (e & f) ^ (~e & g)
      const t1 = (h + S1 + ch + K[i] + w[i]) >>> 0
      const S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22)
      const maj = (a & b) ^ (a & c) ^ (b & c)
      const t2 = (S0 + maj) >>> 0
      ;[h, g, f, e, d, c, b, a] = [g, f, e, (d + t1) >>> 0, c, b, a, (t1 + t2) >>> 0]
    }
    H0 = (H0 + a) >>> 0; H1 = (H1 + b) >>> 0; H2 = (H2 + c) >>> 0; H3 = (H3 + d) >>> 0
    H4 = (H4 + e) >>> 0; H5 = (H5 + f) >>> 0; H6 = (H6 + g) >>> 0; H7 = (H7 + h) >>> 0
  }

  return [H0, H1, H2, H3, H4, H5, H6, H7].map(v => v.toString(16).padStart(8, '0')).join('')
}

// ---------------------------------------------------------------------------
// Trend serialization (the per-encounter recent-form grounding text)
// Extracted from AskPanel so the same logic feeds BOTH display and send.
// ---------------------------------------------------------------------------
function serializeTrend(enc) {
  const win = (enc.windows && (enc.windows[enc.defaultWindow] || enc.windows[6])) || enc.window
  const coh = (enc.coherences && (enc.coherences[enc.defaultWindow] || enc.coherences[6])) || enc.coherence
  const n = win?.windowSize || 0
  const lines = []
  lines.push(`# ${enc.encounterName} (${enc.difficulty}) - recent form (last ${n} pull${n === 1 ? '' : 's'} this night)`)
  for (const r of win?.ruleRows || []) {
    const arrow = r.dir === 'better' ? 'better' : r.dir === 'worse' ? 'worse' : 'flat'
    lines.push(`- ${r.label}: ${r.value} (${arrow} ${r.delta})`)
  }
  const pts = coh?.points || []
  const lastFinite = (key) => {
    for (let i = pts.length - 1; i >= 0; i--) {
      const v = pts[i]?.[key]
      if (typeof v === 'number' && isFinite(v)) return v
    }
    return null
  }
  const fol = lastFinite('followershipMean'), ent = lastFinite('entropyMean'), spd = lastFinite('peakSpeed')
  if (fol != null || ent != null || spd != null) {
    lines.push('## Coordination (movement) over the window')
    if (fol != null) lines.push(`- Followership (how together the raid moves): ${fol.toFixed(3)}`)
    if (ent != null) lines.push(`- Entropy (how spread their movement is): ${ent.toFixed(3)}`)
    if (spd != null) lines.push(`- Peak speed: ${spd.toFixed(1)} yd/s`)
  } else {
    lines.push('- No movement/coordination data for these pulls (advanced combat logging off).')
  }
  lines.push('')
  lines.push("_(Recent-form summary computed by Leopard from Tempo's parser. Every figure above is exact - reflect on these facts, do not infer beyond them.)_")
  return lines.join('\n')
}

function countTrendProperties(enc) {
  const win = (enc.windows && (enc.windows[enc.defaultWindow] || enc.windows[6])) || enc.window
  const coh = (enc.coherences && (enc.coherences[enc.defaultWindow] || enc.coherences[6])) || enc.coherence
  const rowCount = win?.ruleRows?.length || 0
  const pts = coh?.points || []
  const lastFinite = (key) => pts.some(p => typeof p?.[key] === 'number' && isFinite(p[key]))
  const cohCount = ['followershipMean', 'entropyMean', 'peakSpeed'].filter(lastFinite).length
  return rowCount + cohCount
}

// ---------------------------------------------------------------------------
// ContextBuilder → CanonicalContext
// ---------------------------------------------------------------------------
export class ContextBuilder {
  #text = null
  #scopeLabel = 'this raid night'
  #propertyCount = 0

  // For night and career zooms: the markdown blob from the API.
  setText(text, scopeLabel = 'this raid night') {
    this.#text = String(text)
    this.#scopeLabel = scopeLabel
    this.#propertyCount = 1
    return this
  }

  // For the trend zoom: the structured enc object from the trends artifact.
  // serialize() is the canonical text; render() is defined in terms of it.
  setTrend(enc) {
    this.#text = serializeTrend(enc)
    this.#scopeLabel = `recent form on ${enc.encounterName} (${enc.difficulty}) — last pulls this night`
    this.#propertyCount = countTrendProperties(enc)
    return this
  }

  // freeze() returns an immutable CanonicalContext. The builder must not be
  // used after this call (private fields prevent external mutation anyway).
  freeze() {
    if (this.#text === null) throw new Error('ContextBuilder: no content set before freeze()')
    const text = this.#text
    const label = this.#scopeLabel
    const count = this.#propertyCount

    // serialize() is the single source of truth for the bytes.
    // render() is DEFINED IN TERMS OF serialize() — structural, not asserted.
    function serialize() { return text }
    function render() { return React.createElement('pre', { className: 'boxscore' }, serialize()) }
    function digest() {
      return { sha256: sha256hex(serialize()), propertyCount: count, schemaVersion: SCHEMA_VERSION }
    }

    return Object.freeze({ schemaVersion: SCHEMA_VERSION, scopeLabel: label, serialize, render, digest })
  }
}
