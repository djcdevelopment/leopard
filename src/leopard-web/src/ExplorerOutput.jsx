import React, { useState } from 'react'
import { liveObjects } from './knowledge.js'

// The bottom dock: OUTPUT · LLM RESPONSE & TOOLING. Real in phase 1: Response (streamed
// answer + evidence-used line + model/token/latency footer), Sources (the slice manifest +
// digest), Raw JSON (the exact request body + stream stats). Reasoning Trace / Tool Calls /
// Diagnostics render an honest "not captured in phase 1" note.

const TABS = ['Response', 'Reasoning Trace', 'Tool Calls', 'Sources', 'Diagnostics', 'Raw JSON']

// Bold the knowledge-object labels wherever the model mentions them — cosmetic only.
function decorate(answer, labels) {
  if (!answer) return null
  const names = labels.filter((l) => answer.includes(l))
  if (names.length === 0) return answer
  const re = new RegExp(`(${names.map((n) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`, 'g')
  return answer.split(re).map((part, i) => (names.includes(part) ? <b key={i}>{part}</b> : part))
}

export default function ExplorerOutput({ question, answer, busy, stats, sliceItems, digest, request }) {
  const [tab, setTab] = useState('Response')
  const usedLabels = sliceItems.map((s) => s.label)
  const excluded = liveObjects().filter((o) => !sliceItems.some((s) => s.id === o.id)).map((o) => o.label)

  return (
    <section className="ex-output">
      <div className="ex-out-head">Output · LLM response &amp; tooling</div>
      <div className="ex-outtabs">
        {TABS.map((t) => (
          <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>{t}</button>
        ))}
      </div>

      {tab === 'Response' && (
        <div className="ex-response">
          {question && <h3>{question}</h3>}
          {answer
            ? <div className="ex-answer">{decorate(answer, usedLabels)}{busy && <span className="ex-cursor">▋</span>}</div>
            : <p className="muted small">{busy ? 'Thinking…' : 'Run an investigation to see the model\'s reflection here.'}</p>}
          {sliceItems.length > 0 && (
            <p className="ex-evidence small">
              <b>Evidence used:</b> {usedLabels.join(', ') || 'none'}.
              {excluded.length > 0 && <> <b>Excluded:</b> {excluded.join(', ')}.</>}
            </p>
          )}
          {stats && (
            <p className="ex-runmeta mono small">
              {stats.model} · completion ~{stats.completionTok} tok · latency {(stats.latencyMs / 1000).toFixed(1)}s · sources: {sliceItems.length}
            </p>
          )}
        </div>
      )}

      {tab === 'Sources' && (
        <div className="ex-response">
          {sliceItems.length === 0 && <p className="muted small">No slices in the contract yet.</p>}
          {sliceItems.map((s) => (
            <div className="ex-source-row mono small" key={s.id}>
              <span className="x-tag">{s.id}</span> · {s.label} · scope {s.cfg.scope} · rep {s.cfg.rep} · agg {s.cfg.agg} · time {s.cfg.time} · tok {s.tok}
            </div>
          ))}
          {digest && <p className="muted mono small">contract digest sha256:{digest.slice(0, 16)}…</p>}
        </div>
      )}

      {tab === 'Raw JSON' && (
        <div className="ex-response">
          {request
            ? <pre className="ex-raw mono">{JSON.stringify(request, null, 2)}</pre>
            : <p className="muted small">Run an investigation to capture the exact request body.</p>}
        </div>
      )}

      {(tab === 'Reasoning Trace' || tab === 'Tool Calls' || tab === 'Diagnostics') && (
        <div className="ex-response">
          <p className="muted small">Not captured in phase 1 — this dock fills in when the {tab.toLowerCase()} pipeline lands.</p>
        </div>
      )}
    </section>
  )
}
