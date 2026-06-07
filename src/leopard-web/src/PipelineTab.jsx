import React, { useEffect, useState } from 'react'
import { getTrace } from './api.js'
import AskPanel from './AskPanel.jsx'

const fmt = (n) => (typeof n === 'number' ? n.toLocaleString() : n)

export default function PipelineTab({ night, hasParsed, onOpenTab, provider, model }) {
  const [trace, setTrace] = useState(null)
  const [nodeId, setNodeId] = useState('') // land on the overview; drill in on click
  const [status, setStatus] = useState('') // '' | 'loading' | 'unparsed'

  // Re-trace whenever the shared night selection changes.
  useEffect(() => {
    setTrace(null)
    if (!night) { setStatus(''); return }
    setStatus('loading')
    let alive = true
    getTrace(night)
      .then((t) => {
        if (!alive) return
        if (!t) { setStatus('unparsed'); return }
        setTrace(t)
        setStatus('')
        setNodeId('') // overview-first on every night change
      })
      .catch(() => { if (alive) setStatus('unparsed') })
    return () => { alive = false }
  }, [night])

  if (!hasParsed) {
    return (
      <div className="pipeline">
        <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, pick a night, and click <b>PARSE</b>.</p>
      </div>
    )
  }

  const stages = trace?.stages || []
  const projections = trace?.projections || []
  const node =
    stages.find((s) => s.id === nodeId) || projections.find((p) => p.id === nodeId) || null

  return (
    <div className="pipeline">
      {status === 'loading' && <p className="muted">Tracing your log…</p>}
      {status === 'unparsed' && (
        <p className="muted">This night was parsed before the Pipeline Explorer existed — re-parse it in <b>Setup</b> to trace it.</p>
      )}

      {trace && (
        <>
          <p className="muted small">
            Your log, traced — how {fmt(trace.totals.rawLines)} raw lines become {fmt(trace.totals.keptEvents)} kept
            events across {fmt(trace.totals.pulls)} pull{trace.totals.pulls === 1 ? '' : 's'}. Click any node to watch
            your own data flow through it. <span className="dim">Counts are exact; samples show the first few rows · traced in {trace.walkSeconds}s.</span>
          </p>

          <div className="flowmap">
            {stages.map((s, i) => (
              <React.Fragment key={s.id}>
                {i > 0 && <span className="flowarrow">→</span>}
                <StageNode node={s} active={s.id === nodeId} onClick={() => setNodeId(s.id)} />
              </React.Fragment>
            ))}
          </div>

          <div className="fanout">
            <span className="fanlabel muted">fans out to</span>
            {projections.map((p) => (
              <button
                key={p.id}
                className={`projnode ${p.id === nodeId ? 'active' : ''}`}
                onClick={() => setNodeId(p.id)}
              >
                {p.title}
                {typeof p.count === 'number' && <span className="pn-count">{fmt(p.count)}</span>}
              </button>
            ))}
          </div>

          {node ? <DrillIn node={node} onOpenTab={onOpenTab} night={night} hasParsed={hasParsed} provider={provider} model={model} /> : (
            <p className="muted di-hint">Click any node above to watch your data flow through it — start with <b>Trim</b>, the dramatic collapse.</p>
          )}
        </>
      )}
    </div>
  )
}

function StageNode({ node, active, onClick }) {
  return (
    <button className={`stagenode ${active ? 'active' : ''}`} onClick={onClick}>
      <span className="sn-title">{node.title}</span>
      <span className="sn-flow">
        <span className="sn-in">{fmt(node.countIn)}</span>
        <span className="sn-arr">↓</span>
        <span className="sn-out">{fmt(node.countOut)}</span>
      </span>
      <span className="sn-emits">{node.emitsLabel}</span>
    </button>
  )
}

function DrillIn({ node, onOpenTab, night, hasParsed, provider, model }) {
  const isStage = 'countIn' in node
  const isAsk = node.id === 'boxscore' // the terminus: ask the box score at the end of the chain

  return (
    <div className="drillin">
      <h3>{node.title}</h3>
      <p className="di-does">{node.does}</p>

      {isStage ? (
        <div className="di-io">
          <div className="di-box">
            <span className="muted">sees</span>
            <b>{fmt(node.countIn)}</b>
            <span className="dim">{node.seesLabel}</span>
          </div>
          <span className="di-arrow">→</span>
          <div className="di-box emit">
            <span className="muted">emits</span>
            <b>{fmt(node.countOut)}</b>
            <span className="dim">{node.emitsLabel}</span>
          </div>
        </div>
      ) : isAsk ? (
        // The box-score boundary. Left of here is exact/computed; right is a constrained model
        // speaking. The pipeline guarantees the numbers going IN, not the words coming OUT — so
        // this "feeds" Ask, it does not "emit" the answer.
        <div className="di-ask">
          <p className="di-boundary muted small">
            ┄ the boundary ┄ everything left is exact and computed; this <b>feeds</b> the local model,
            which speaks in words. <b>The pipeline guarantees the numbers going in — not the words coming out.</b>
          </p>
          {provider?.status === 'ready'
            ? <span className="di-modelnote muted small">via {model || 'local model'}</span>
            : null}
          <AskPanel provider={provider || { status: 'checking', models: [] }} model={model} night={night} hasParsed={hasParsed} allowZoom={false} />
          {onOpenTab && (
            <button className="link di-fulltab" onClick={() => onOpenTab('leopard')}>or open the full Ask tab (career + recent-form zooms) →</button>
          )}
        </div>
      ) : (
        <div className="di-io">
          {typeof node.count === 'number' && (
            <div className="di-box"><b>{fmt(node.count)}</b><span className="dim">events</span></div>
          )}
          {node.tab && (
            <button className="di-open" onClick={() => onOpenTab?.(node.tab)}>
              Open the {node.title} tab →
            </button>
          )}
        </div>
      )}

      {isStage && <Sample kind={node.sampleKind} rows={node.sample} />}
    </div>
  )
}

function Sample({ kind, rows }) {
  if (!rows || rows.length === 0) return null

  if (kind === 'raw') {
    return (
      <div className="di-sample">
        <p className="muted small">A sample of the raw lines:</p>
        <pre className="rawlines">{rows.join('\n')}</pre>
      </div>
    )
  }

  if (kind === 'event') {
    return (
      <div className="di-sample">
        <p className="muted small">A sample of classified events:</p>
        <table className="sampletbl">
          <thead><tr><th>event</th><th>spell</th><th className="num">amount</th><th>source → dest</th></tr></thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={i}>
                <td className="mono">{r.kind}</td>
                <td>{r.spell || <span className="dim">—</span>}</td>
                <td className="num">{r.amount ? fmt(r.amount) : <span className="dim">—</span>}</td>
                <td className="mono dim">{r.src || '—'} → {r.dst || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  if (kind === 'segment') {
    return (
      <div className="di-sample">
        <p className="muted small">Every pull this night, with its pre-trim event count:</p>
        <table className="sampletbl">
          <thead><tr><th className="num">#</th><th>boss</th><th>outcome</th><th className="num">events</th></tr></thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={i}>
                <td className="num">{r.n}</td>
                <td>{r.encounter || <span className="dim">—</span>}</td>
                <td className={r.outcome === 'Kill' ? 'ok' : ''}>{r.outcome}</td>
                <td className="num">{fmt(r.events)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  if (kind === 'trim') {
    return (
      <div className="di-sample">
        <p className="muted small">The collapse, per pull — pre-trim events down to what's kept:</p>
        <table className="sampletbl">
          <thead><tr><th className="num">#</th><th>boss</th><th className="num">pre-trim</th><th className="num">kept</th><th className="num">% kept</th></tr></thead>
          <tbody>
            {rows.map((r, i) => {
              const pct = r.preTrim > 0 ? (100 * r.kept / r.preTrim) : 0
              return (
                <tr key={i}>
                  <td className="num">{r.n}</td>
                  <td>{r.encounter || <span className="dim">—</span>}</td>
                  <td className="num">{fmt(r.preTrim)}</td>
                  <td className="num accent">{fmt(r.kept)}</td>
                  <td className="num dim">{pct.toFixed(pct < 1 ? 2 : 1)}%</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    )
  }

  return null
}
