import React, { useEffect, useState } from 'react'
import { detectProvider, chatStream, loadedModels } from './provider.js'
import { getBoxscore } from './api.js'
import { SEEDED_QUESTIONS } from './evidence.js'
import { buildMessages } from './prompt.js'

export default function LeopardTab({ night, hasParsed }) {
  const [provider, setProvider] = useState({ status: 'checking', models: [] })
  const [model, setModel] = useState('')
  const [evidence, setEvidence] = useState('')
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    detectProvider().then(async (p) => {
      setProvider({ status: p.reachable ? 'ready' : 'absent', models: p.models, error: p.error })
      if (p.reachable && p.models?.length) {
        // Prefer a model already resident in the provider (e.g. the warmed *-b70 from the
        // local-inference runbook) so the first Ask doesn't pay a cold load. Otherwise fall
        // back to a preference list — local large models first, then a capable qwen.
        const loaded = await loadedModels()
        const warm = loaded.find((m) => p.models.includes(m))
        const pref = [/b70/i, /mistral/i, /qwen2\.5[:\-]?14b/i, /:14b/i, /qwen2\.5[:\-]?32b/i, /:32b/i, /instruct/i]
        setModel(warm || pref.map((re) => p.models.find((m) => re.test(m))).find(Boolean) || p.models[0])
      }
    })
  }, [])

  // The box score is the exact text the model is grounded in; re-read it whenever the shared
  // night selection changes.
  useEffect(() => {
    setEvidence('')
    setAnswer('')
    if (!night) return
    let alive = true
    getBoxscore(night)
      .then((b) => { if (alive) setEvidence(b) })
      .catch(() => { if (alive) setEvidence('') })
    return () => { alive = false }
  }, [night])

  const canAsk = provider.status === 'ready' && !!evidence && !busy
  async function ask(q) {
    const qq = (q ?? question).trim()
    if (!qq || !canAsk) return
    setQuestion(qq)
    setAnswer('')
    setBusy(true)
    try {
      await chatStream({ model, messages: buildMessages(qq, evidence), onToken: (t) => setAnswer((a) => a + t) })
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

      {evidence && (
        <>
          <details className="evidence-panel" open>
            <summary>What Leopard reads for this night <span className="muted">— the exact text sent to the model</span></summary>
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
                placeholder="Ask anything about this raid…"
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
