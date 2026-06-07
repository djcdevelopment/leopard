import React, { useEffect, useState } from 'react'
import { detectProvider, chatStream, loadedModels } from './provider.js'
import { getBoxscore, getCareerSummary, getCareer } from './api.js'
import { SEEDED_QUESTIONS } from './evidence.js'
import { buildMessages } from './prompt.js'

// Ask grounds at a chosen ZOOM: a single night (the box score, default) or a boss's all-time
// career arc. The night is the smallest zoom; the career arc is what answers "are we getting
// better at this boss?" — the question one night structurally cannot. Same grounding contract at
// every zoom (exact figures, restate-don't-infer, stay in scope). See docs + the zoom plan.

export default function LeopardTab({ night, hasParsed }) {
  const [provider, setProvider] = useState({ status: 'checking', models: [] })
  const [model, setModel] = useState('')
  const [zoom, setZoom] = useState('night') // 'night' | 'career'
  const [careers, setCareers] = useState([])
  const [careerId, setCareerId] = useState('')
  const [evidence, setEvidence] = useState('')
  const [scopeLabel, setScopeLabel] = useState('this raid night')
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    detectProvider().then(async (p) => {
      setProvider({ status: p.reachable ? 'ready' : 'absent', models: p.models, error: p.error })
      if (p.reachable && p.models?.length) {
        // Prefer a model already resident in the provider (the warmed *-b70 from the runbook) so
        // the first Ask doesn't pay a cold load. Else a preference list — local large models first.
        const loaded = await loadedModels()
        const warm = loaded.find((m) => p.models.includes(m))
        const pref = [/b70/i, /mistral/i, /qwen2\.5[:\-]?14b/i, /:14b/i, /qwen2\.5[:\-]?32b/i, /:32b/i, /instruct/i]
        setModel(warm || pref.map((re) => p.models.find((m) => re.test(m))).find(Boolean) || p.models[0])
      }
    })
  }, [])

  // Lazily load the all-time boss list the first time the career zoom is opened.
  useEffect(() => {
    if (zoom !== 'career' || careers.length > 0) return
    getCareer()
      .then((d) => {
        const bs = d.bosses || []
        setCareers(bs)
        setCareerId((cur) => cur || bs[0]?.careerId || '')
      })
      .catch(() => {})
  }, [zoom]) // eslint-disable-line react-hooks/exhaustive-deps

  // Load the grounding evidence for the current zoom. This is the EXACT text sent to the model.
  useEffect(() => {
    setEvidence('')
    setAnswer('')
    let alive = true
    if (zoom === 'night') {
      setScopeLabel('this raid night')
      if (!night) return
      getBoxscore(night).then((b) => { if (alive) setEvidence(b) }).catch(() => { if (alive) setEvidence('') })
    } else {
      if (!careerId) return
      const b = careers.find((c) => c.careerId === careerId)
      setScopeLabel(b ? `${b.name} (${b.difficulty}) — all-time career` : "this boss's career")
      getCareerSummary(careerId).then((t) => { if (alive) setEvidence(t || '') }).catch(() => { if (alive) setEvidence('') })
    }
    return () => { alive = false }
  }, [zoom, night, careerId, careers])

  const canAsk = provider.status === 'ready' && !!evidence && !busy
  async function ask(q) {
    const qq = (q ?? question).trim()
    if (!qq || !canAsk) return
    setQuestion(qq)
    setAnswer('')
    setBusy(true)
    try {
      await chatStream({ model, messages: buildMessages(qq, evidence, scopeLabel), onToken: (t) => setAnswer((a) => a + t) })
    } catch (e) {
      setAnswer(`(error talking to the model: ${e?.message || e})`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="leopard">
      <ProviderBar provider={provider} model={model} setModel={setModel} />

      {!hasParsed && (
        <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, pick a night, and click <b>PARSE</b>.</p>
      )}

      {hasParsed && (
        <div className="wintoggle ask-zoom">
          <span className="muted small">Ask about:</span>
          <button className={zoom === 'night' ? 'active' : ''} onClick={() => setZoom('night')}>This night</button>
          <button className={zoom === 'career' ? 'active' : ''} onClick={() => setZoom('career')}>A boss’s career</button>
          {zoom === 'career' && careers.length > 0 && (
            <select className="zoom-boss" value={careerId} onChange={(e) => setCareerId(e.target.value)}>
              {careers.map((c) => <option key={c.careerId} value={c.careerId}>{c.name} · {c.difficulty}</option>)}
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
                placeholder={zoom === 'career' ? 'Ask about this boss’s progression…' : 'Ask anything about this raid…'}
              />
              <button disabled={!canAsk} onClick={() => ask()}>{busy ? '…' : 'Ask'}</button>
            </div>
            {answer && <div className="answer">{answer}</div>}
          </section>
        </>
      )}
    </div>
  )
}

function ProviderBar({ provider, model, setModel }) {
  if (provider.status === 'checking') return <div className="bar checking">Looking for a local model…</div>
  if (provider.status === 'absent')
    return <div className="bar absent">No local model found — start Ollama and reload. Your data never leaves your machine.</div>
  return (
    <div className="bar ready">
      Local model:&nbsp;
      <select value={model} onChange={(e) => setModel(e.target.value)}>
        {provider.models.map((m) => <option key={m} value={m}>{m}</option>)}
      </select>
      &nbsp;· on your machine · nothing leaves your PC
    </div>
  )
}
