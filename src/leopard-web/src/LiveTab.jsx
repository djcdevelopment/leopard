import React, { useEffect, useRef, useState } from 'react'
import { getLiveStatus, getLiveInsight, postLiveFeedback } from './api.js'

// The live review desk: while raiding (/combatlog on), the host watches the log via Tempo's
// live ingest, pre-generates one grounded insight per pull on the local model, and this tab
// shows the latest card. The operator grades it on two NAMED axes — useful (worth reading
// between pulls?) and grounded (did it stick to the evidence?) — plus an optional note.
// Evidence is shown byte-identical to what the model read (display==send, as everywhere).
// See docs/live-insight-design-brief.md.

function fmtDur(ms) {
  if (ms == null) return ''
  const s = Math.round(ms / 1000)
  return s >= 3600 ? `${Math.floor(s / 3600)}h${String(Math.floor((s % 3600) / 60)).padStart(2, '0')}m`
    : `${Math.floor(s / 60)}m${String(s % 60).padStart(2, '0')}s`
}

export default function LiveTab() {
  const [status, setStatus] = useState(null)
  const [insight, setInsight] = useState(null)
  const [useful, setUseful] = useState(null)     // true | false | null
  const [grounded, setGrounded] = useState(null) // true | false | null
  const [comment, setComment] = useState('')
  const [sent, setSent] = useState(false)
  const lastInsightId = useRef(null)

  // Status poll (5s) — cheap; runs while the tab is mounted.
  useEffect(() => {
    let alive = true
    const tick = () => getLiveStatus().then((s) => { if (alive) setStatus(s) }).catch(() => {})
    tick()
    const t = setInterval(tick, 5000)
    return () => { alive = false; clearInterval(t) }
  }, [])

  // Insight poll (3s) while there's a live session.
  useEffect(() => {
    if (!status?.active && !status?.file) return
    let alive = true
    const tick = () => getLiveInsight().then((i) => { if (alive) setInsight(i) }).catch(() => {})
    tick()
    const t = setInterval(tick, 3000)
    return () => { alive = false; clearInterval(t) }
  }, [status?.active, status?.file])

  // A new pull's insight re-arms the feedback controls.
  useEffect(() => {
    const id = insight?.insightId
    if (id && id !== lastInsightId.current) {
      lastInsightId.current = id
      setUseful(null); setGrounded(null); setComment(''); setSent(false)
    }
  }, [insight?.insightId])

  async function send() {
    if (!insight?.insightId) return
    try {
      await postLiveFeedback({ insightId: insight.insightId, useful, grounded, comment: comment || null })
      setSent(true)
    } catch { /* keep the controls; the operator can retry */ }
  }

  const pulls = status?.pulls || []
  const pull = insight?.pull

  if (!status) return <p className="muted">Checking the live session…</p>

  return (
    <div className="live">
      {/* Session line */}
      {status.file ? (
        <p className="muted small">
          Watching <b>{status.file}</b>
          {status.inEncounter && status.currentBoss && <> · <span className="live-pulling">pull in progress — {status.currentBoss}</span></>}
          {!status.active && ' · no recent log activity (is /combatlog on?)'}
        </p>
      ) : (
        <p className="muted">No live session — enable <b>/combatlog</b> in WoW and start a pull.
          The card will be waiting here (and on the overlay) when it ends.</p>
      )}

      {/* The card */}
      {insight && insight.state !== 'none' && pull && (
        <section className="live-card">
          <div className="live-head">
            <b>{pull.boss}</b>
            <span className={pull.kill ? 'live-kill' : 'live-wipe'}>{pull.kill ? 'KILL ✓' : 'WIPE ✗'}</span>
            <span className="muted">{pull.difficulty} · {fmtDur(pull.durationMs)} · {pull.playerDeaths} ☠
              {pull.bossEndPct != null && !pull.kill && <> · boss ~{pull.bossEndPct}%</>}
              {' '}· pull {pull.tonightIndex} tonight</span>
          </div>

          {insight.state === 'pending' && (
            <p className="muted">Generating insight on the local model…</p>
          )}
          {insight.state === 'error' && (
            <p className="live-error">Insight failed: {insight.error} — the next pull retries.
              (Is the inference server up? See Setup for the live endpoint.)</p>
          )}
          {insight.state === 'ready' && (
            <>
              <p className="live-text">{insight.text}</p>

              <details className="raw-context">
                <summary className="muted small">What the model knew — the exact evidence sent</summary>
                <pre className="boxscore">{insight.evidence}</pre>
              </details>

              {sent ? (
                <p className="muted small">Got it — recorded for {insight.insightId}.</p>
              ) : (
                <div className="live-feedback">
                  <span className="muted small">Useful?</span>
                  <button className={`tap ${useful === true ? 'on' : ''}`} onClick={() => setUseful(true)}>👍</button>
                  <button className={`tap ${useful === false ? 'on' : ''}`} onClick={() => setUseful(false)}>👎</button>
                  <span className="muted small">Grounded?</span>
                  <button className={`tap ${grounded === true ? 'on' : ''}`} onClick={() => setGrounded(true)}>Yes</button>
                  <button className={`tap ${grounded === false ? 'on' : ''}`} onClick={() => setGrounded(false)}>No</button>
                  <input
                    className="live-comment" maxLength={140} placeholder="optional note (140)"
                    value={comment} onChange={(e) => setComment(e.target.value)}
                    onKeyDown={(e) => { if (e.key === 'Enter') send() }}
                  />
                  <button className="live-send" disabled={useful === null && grounded === null && !comment}
                    onClick={send}>Send</button>
                </div>
              )}
            </>
          )}
        </section>
      )}

      {/* Tonight so far */}
      {pulls.length > 0 && (
        <section className="live-tonight">
          <h3>Tonight (observed live)</h3>
          <table className="grid">
            <thead><tr><th>#</th><th>Boss</th><th>Result</th><th>Deaths</th><th>Duration</th></tr></thead>
            <tbody>
              {[...pulls].reverse().map((p) => (
                <tr key={p.pullId}>
                  <td>{p.tonightIndex}</td>
                  <td>{p.boss} <span className="muted">({p.difficulty})</span></td>
                  <td>{p.kill ? 'Kill' : p.bossEndPct != null ? `Wipe · ${p.bossEndPct}%` : 'Wipe'}</td>
                  <td>{p.playerDeaths}</td>
                  <td>{fmtDur(p.durationMs)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  )
}
