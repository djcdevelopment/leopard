import React, { useEffect, useRef, useState } from 'react'
import { chatStream } from './provider.js'
import { getBoxscore, getCareerSummary, getCareer, getTrends } from './api.js'
import { SEEDED_QUESTIONS } from './evidence.js'
import { buildMessages } from './prompt.js'

// The reusable Ask experience, grounded at a chosen ZOOM. Rendered on the Leopard tab (allowZoom,
// the full front door) and embedded at the Pipeline's terminus (allowZoom=false, night only). The
// displayed evidence is byte-identical to what's sent to the model. Provider/model are owned by App
// (one detect per load, shared across both Ask surfaces). The stream aborts on unmount / new ask.
export default function AskPanel({ provider, model, night, hasParsed, allowZoom = true }) {
  const [zoom, setZoom] = useState('night') // 'night' | 'career' | 'trend'
  const [careers, setCareers] = useState([])
  const [careerId, setCareerId] = useState('')
  const [trendEncs, setTrendEncs] = useState([]) // this night's boss encounters (trend zoom)
  const [trendId, setTrendId] = useState('')
  const [trendData, setTrendData] = useState(null)
  const [evidence, setEvidence] = useState('')
  const [scopeLabel, setScopeLabel] = useState('this raid night')
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState('')
  const [busy, setBusy] = useState(false)
  const abortRef = useRef(null)

  useEffect(() => () => abortRef.current?.abort(), [])

  // Lazily load the all-time boss list when the career zoom is first opened.
  useEffect(() => {
    if (zoom !== 'career' || careers.length > 0) return
    getCareer()
      .then((d) => { const bs = d.bosses || []; setCareers(bs); setCareerId((c) => c || bs[0]?.careerId || '') })
      .catch(() => {})
  }, [zoom]) // eslint-disable-line react-hooks/exhaustive-deps

  // Load this night's trends artifact when the trend zoom is opened or the night changes.
  useEffect(() => {
    if (zoom !== 'trend' || !night) return
    getTrends(night)
      .then((t) => {
        const encs = t?.encounters || []
        setTrendData(t)
        setTrendEncs(encs)
        setTrendId((cur) => (encs.some((e) => e.encounterId === cur) ? cur : encs[0]?.encounterId || ''))
      })
      .catch(() => { setTrendData(null); setTrendEncs([]) })
  }, [zoom, night])

  // Evidence per zoom — the exact text sent to the model (display == send).
  useEffect(() => {
    setEvidence('')
    setAnswer('')
    let alive = true
    if (zoom === 'night') {
      setScopeLabel('this raid night')
      if (!night) return
      getBoxscore(night).then((b) => { if (alive) setEvidence(b) }).catch(() => { if (alive) setEvidence('') })
    } else if (zoom === 'career') {
      if (!careerId) return
      const b = careers.find((c) => c.careerId === careerId)
      setScopeLabel(b ? `${b.name} (${b.difficulty}) — all-time career` : "this boss's career")
      getCareerSummary(careerId).then((t) => { if (alive) setEvidence(t || '') }).catch(() => { if (alive) setEvidence('') })
    } else if (zoom === 'trend') {
      const enc = trendEncs.find((e) => e.encounterId === trendId)
      if (!enc) return
      setScopeLabel(`recent form on ${enc.encounterName} (${enc.difficulty}) — last pulls this night`)
      setEvidence(trendSummaryText(enc))
    }
    return () => { alive = false }
  }, [zoom, night, careerId, careers, trendId, trendEncs])

  const canAsk = provider.status === 'ready' && !!evidence && !busy
  async function ask(q) {
    const qq = (q ?? question).trim()
    if (!qq || !canAsk) return
    setQuestion(qq)
    setAnswer('')
    setBusy(true)
    abortRef.current?.abort()
    const ctrl = new AbortController()
    abortRef.current = ctrl
    try {
      await chatStream({ model, messages: buildMessages(qq, evidence, scopeLabel), signal: ctrl.signal, onToken: (t) => setAnswer((a) => a + t) })
    } catch (e) {
      if (e?.name !== 'AbortError') setAnswer(`(error talking to the model: ${e?.message || e})`)
    } finally {
      setBusy(false)
    }
  }

  if (!hasParsed) {
    return <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, pick a night, and click <b>PARSE</b>.</p>
  }

  return (
    <>
      {allowZoom && (
        <div className="wintoggle ask-zoom">
          <span className="muted small">Ask about:</span>
          <button className={zoom === 'night' ? 'active' : ''} onClick={() => setZoom('night')}>This night</button>
          <button className={zoom === 'career' ? 'active' : ''} onClick={() => setZoom('career')}>A boss’s career</button>
          <button className={zoom === 'trend' ? 'active' : ''} onClick={() => setZoom('trend')}>Recent form</button>
          {zoom === 'career' && careers.length > 0 && (
            <select className="zoom-boss" value={careerId} onChange={(e) => setCareerId(e.target.value)}>
              {careers.map((c) => <option key={c.careerId} value={c.careerId}>{c.name} · {c.difficulty}</option>)}
            </select>
          )}
          {zoom === 'trend' && trendEncs.length > 0 && (
            <select className="zoom-boss" value={trendId} onChange={(e) => setTrendId(e.target.value)}>
              {trendEncs.map((e) => <option key={e.encounterId} value={e.encounterId}>{e.encounterName} · {e.difficulty}</option>)}
            </select>
          )}
        </div>
      )}

      {evidence && (
        <>
          <details className="evidence-panel" open>
            <summary>What Leopard reads <span className="muted">— the exact text sent to the model · {scopeLabel}</span></summary>
            <p className="muted small">Distilled from your combat log by the parser into these exact figures. Your question is appended below this — nothing else is sent to the model.</p>
            <pre className="boxscore">{evidence}</pre>
          </details>

          <section className="ask">
            <div className="seeds">
              {SEEDED_QUESTIONS.map((q) => (
                <button key={q} disabled={!canAsk} onClick={() => ask(q)}>{q}</button>
              ))}
            </div>
            <div className="askbox">
              <input
                value={question}
                onChange={(e) => setQuestion(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') ask() }}
                placeholder={zoom === 'night' ? 'Ask anything about this raid…' : 'Ask about this boss…'}
              />
              <button disabled={!canAsk} onClick={() => ask()}>{busy ? '…' : 'Ask'}</button>
            </div>
            {answer && <div className="answer">{answer}</div>}
          </section>
        </>
      )}
    </>
  )
}

// Recent-form grounding text rendered from the per-night trends artifact — the rule-row deltas plus
// the coordination signals (followership / entropy / peak speed) that the box score and career arc
// don't carry. Built once and used for BOTH display and the model (the grounding invariant).
function trendSummaryText(enc) {
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
  const lastFinite = (key) => { for (let i = pts.length - 1; i >= 0; i--) { const v = pts[i]?.[key]; if (typeof v === 'number' && isFinite(v)) return v } return null }
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
  lines.push('_(Recent-form summary computed by Leopard from Tempo\'s parser. Every figure above is exact - reflect on these facts, do not infer beyond them.)_')
  return lines.join('\n')
}
