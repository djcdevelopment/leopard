import React, { useEffect, useRef, useState } from 'react'
import { chatStream } from './provider.js'
import { getBoxscore, getCareer, getTrends } from './api.js'
import { SEEDED_QUESTIONS } from './evidence.js'
import { buildMessages } from './prompt.js'
import { ContextBuilder } from './context.js'
import { CAREER_PALETTE, CAREER_DEFAULT_IDS, buildCareerLens } from './lens.js'

// The reusable Ask experience, grounded at a chosen ZOOM. Rendered on the Leopard tab (allowZoom,
// the full front door) and embedded at the Pipeline's terminus (allowZoom=false, night only). The
// displayed evidence is byte-identical to what's sent to the model — enforced structurally by
// CanonicalContext: render() is defined in terms of serialize(), so they cannot drift.
// Provider/model are owned by App (one detect per load, shared across both Ask surfaces).
export default function AskPanel({ provider, model, night, hasParsed, allowZoom = true }) {
  const [zoom, setZoom] = useState('night') // 'night' | 'career' | 'trend'
  const [careers, setCareers] = useState([])
  const [careerId, setCareerId] = useState('')
  const [trendEncs, setTrendEncs] = useState([]) // this night's boss encounters (trend zoom)
  const [trendId, setTrendId] = useState('')
  const [context, setContext] = useState(null) // CanonicalContext | null
  // career lens — which properties the user has selected, and their rendered display lines
  const [selectedCareerProps, setSelectedCareerProps] = useState(CAREER_DEFAULT_IDS)
  const [displayItems, setDisplayItems] = useState([]) // [{id, label, valueLabel}]
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState('')
  const [busy, setBusy] = useState(false)
  const abortRef = useRef(null)
  // Track zoom in a ref so the night-change effect can read current zoom without
  // listing zoom as a dependency (which would cause resets on every zoom switch).
  const zoomRef = useRef(zoom)
  useEffect(() => { zoomRef.current = zoom }, [zoom])
  // Career zoom is all-time, not night-scoped — staying on it when the user picks
  // a new night leaves stale-feeling data on screen. Snap back to night zoom so
  // the newly selected log's data is front-and-center.
  const prevNightRef = useRef(night)
  useEffect(() => {
    const prev = prevNightRef.current
    prevNightRef.current = night
    if (!prev || prev === night) return
    if (zoomRef.current === 'career') setZoom('night')
  }, [night])

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
        setTrendEncs(encs)
        setTrendId((cur) => (encs.some((e) => e.encounterId === cur) ? cur : encs[0]?.encounterId || ''))
      })
      .catch(() => { setTrendEncs([]) })
  }, [zoom, night])

  // Build a CanonicalContext per zoom.
  // Career zoom: synchronous — builds XML from roster data already in state.
  // Night/trend: async fetch, then build from the returned blob/enc.
  useEffect(() => {
    setContext(null)
    setAnswer('')
    setDisplayItems([])
    let alive = true

    if (zoom === 'night') {
      if (!night) return
      getBoxscore(night)
        .then((b) => { if (alive) setContext(new ContextBuilder().setText(b, 'this raid night').freeze()) })
        .catch(() => {})
      return () => { alive = false }
    }

    if (zoom === 'career') {
      if (!careerId || careers.length === 0) return
      const boss = careers.find((c) => c.careerId === careerId)
      if (!boss) return
      const { xml, displayItems: items, propertyCount } = buildCareerLens(boss, selectedCareerProps)
      const label = `${boss.name} (${boss.difficulty}) — all-time career`
      setContext(new ContextBuilder().setText(xml, label, propertyCount).freeze())
      setDisplayItems(items)
      return
    }

    if (zoom === 'trend') {
      const enc = trendEncs.find((e) => e.encounterId === trendId)
      if (!enc) return
      setContext(new ContextBuilder().setTrend(enc).freeze())
      return
    }
  }, [zoom, night, careerId, careers, selectedCareerProps, trendId, trendEncs])

  const canAsk = provider.status === 'ready' && !!context && !busy
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
      await chatStream({
        model,
        messages: buildMessages(qq, context.serialize(), context.scopeLabel),
        signal: ctrl.signal,
        onToken: (t) => setAnswer((a) => a + t),
      })
    } catch (e) {
      if (e?.name !== 'AbortError') setAnswer(`(error talking to the model: ${e?.message || e})`)
    } finally {
      setBusy(false)
    }
  }

  function toggleCareerProp(id, checked) {
    setSelectedCareerProps(prev => {
      const next = new Set(prev)
      if (checked) next.add(id)
      else next.delete(id)
      return next
    })
  }

  if (!hasParsed) {
    return <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, pick a night, and click <b>PARSE</b>.</p>
  }

  const isCareerComposer = zoom === 'career' && displayItems.length > 0

  return (
    <>
      {allowZoom && (
        <div className="wintoggle ask-zoom">
          <span className="muted small">Ask about:</span>
          <button className={zoom === 'night' ? 'active' : ''} onClick={() => setZoom('night')}>This night</button>
          <button className={zoom === 'career' ? 'active' : ''} onClick={() => setZoom('career')}>A boss's career</button>
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

      {/* Career property palette — "Tell Leopard what matters" */}
      {allowZoom && zoom === 'career' && careers.length > 0 && (
        <div className="career-palette">
          <span className="muted small">Tell Leopard what matters:</span>
          <div className="career-props">
            {CAREER_PALETTE.map(prop => (
              <label key={prop.id} className="prop-toggle">
                <input
                  type="checkbox"
                  checked={selectedCareerProps.has(prop.id)}
                  onChange={e => toggleCareerProp(prop.id, e.target.checked)}
                />
                {prop.label}
              </label>
            ))}
          </div>
        </div>
      )}

      {context && (
        <>
          <details className="evidence-panel" open>
            <summary>
              {isCareerComposer ? 'What Leopard knows' : 'What Leopard reads'}
              <span className="muted"> — {context.scopeLabel}</span>
            </summary>

            {isCareerComposer ? (
              <>
                <ul className="lens-summary">
                  {displayItems.map(({ id, label, valueLabel }) => (
                    <li key={id}><span className="lens-check">✓</span> <b>{label}:</b> {valueLabel}</li>
                  ))}
                </ul>
                <details className="raw-context">
                  <summary className="muted small">Show raw context (XML sent to model)</summary>
                  <pre className="boxscore">{context.serialize()}</pre>
                </details>
              </>
            ) : (
              <>
                <p className="muted small">Distilled from your combat log by the parser into these exact figures. Your question is appended below this — nothing else is sent to the model.</p>
                {context.render()}
              </>
            )}
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
