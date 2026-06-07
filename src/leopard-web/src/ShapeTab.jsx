import React, { useEffect, useState } from 'react'
import { getShapeDensity, getShapeWkDelta } from './api.js'

// The Shape surface: a per-pull density heatmap (the long-exposure of a pull) plus a
// career-scoped kill-vs-wipe contrast. Honesty rules baked in (see docs/shape-design-brief.md):
// crisp sparse cells (no smoothing), self-scaled per-pull arena, no "averaged" at N=1, and a
// first-class one-sided frame when a boss was only wiped (or only killed).

const THIN_SAMPLES = 800 // below this, the heatmap is sparse enough to caption as thin

export default function ShapeTab({ night, hasParsed }) {
  const [data, setData] = useState(null)
  const [encId, setEncId] = useState('')
  const [pullIdx, setPullIdx] = useState(0)
  const [status, setStatus] = useState('') // '' | 'loading' | 'unparsed'
  const [wk, setWk] = useState(null)
  const [wkStatus, setWkStatus] = useState('') // '' | 'loading' | 'none'

  // Density artifact for the selected night.
  useEffect(() => {
    setData(null); setEncId(''); setPullIdx(0)
    if (!night) { setStatus(''); return }
    setStatus('loading')
    let alive = true
    getShapeDensity(night)
      .then((d) => {
        if (!alive) return
        if (!d) { setStatus('unparsed'); return }
        setData(d); setStatus('')
        const encs = d.encounters || []
        const def = encs.find((e) => e.kills === 0 && e.pullCount > 0) || encs[0]
        if (def) setEncId(def.encounterId)
      })
      .catch(() => { if (alive) setStatus('unparsed') })
    return () => { alive = false }
  }, [night])

  const encounters = data?.encounters || []
  const enc = encounters.find((e) => e.encounterId === encId) || null

  // Career-scoped kill-vs-wipe for the selected boss (fanned across all nights).
  useEffect(() => {
    setWk(null)
    if (!enc) { setWkStatus(''); return }
    setPullIdx(0)
    setWkStatus('loading')
    let alive = true
    getShapeWkDelta(enc.careerId)
      .then((w) => { if (alive) { setWk(w); setWkStatus(w ? '' : 'none') } })
      .catch(() => { if (alive) setWkStatus('none') })
    return () => { alive = false }
  }, [encId]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!hasParsed) {
    return (
      <div className="shape">
        <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, pick a night, and click <b>PARSE</b>.</p>
      </div>
    )
  }

  const pulls = enc?.pulls || []
  const pull = pulls[Math.min(pullIdx, pulls.length - 1)] || null

  return (
    <div className="shape">
      {encounters.length > 0 && (
        <div className="picker">
          <label>Boss:&nbsp;
            <select value={encId} onChange={(e) => setEncId(e.target.value)}>
              {encounters.map((e) => (
                <option key={e.encounterId} value={e.encounterId}>
                  {e.encounterName}{e.kills > 0 ? ' · killed' : ''} ({e.pullCount} pull{e.pullCount === 1 ? '' : 's'} this night)
                </option>
              ))}
            </select>
          </label>
        </div>
      )}

      {status === 'loading' && <p className="muted">Reading shapes…</p>}
      {status === 'unparsed' && (
        <p className="muted">This night was parsed before Shape existed — re-parse it in <b>Setup</b> to see it.</p>
      )}

      {enc && (
        <>
          <section className="shape-hero">
            <h3>Where the raid stood <span className="muted small">— the long-exposure of one pull</span></h3>
            {pulls.length > 0 && (
              <div className="picker">
                <label>Pull:&nbsp;
                  <select value={Math.min(pullIdx, pulls.length - 1)} onChange={(e) => setPullIdx(Number(e.target.value))}>
                    {pulls.map((p, i) => (
                      <option key={p.pullId} value={i}>{pullLabel(p)}{p.hasMovement ? '' : ' · no movement'}</option>
                    ))}
                  </select>
                </label>
              </div>
            )}
            {pull?.density ? <Heatmap d={pull.density} /> : (
              <p className="muted small">No movement data for this pull — advanced combat logging wasn't on, or no positions were recorded.</p>
            )}
          </section>

          <section className="shape-wk">
            <h3>Kill vs Wipe <span className="muted small">— {enc.encounterName}, all-time across every night</span></h3>
            {wkStatus === 'loading' && <p className="muted small">Reading every attempt…</p>}
            {wkStatus === 'none' && <p className="muted small">No career contrast for this boss yet.</p>}
            {wk && <WkDelta wk={wk} />}
          </section>
        </>
      )}
    </div>
  )
}

// Crisp cell grid — honest to the sparse 32x16 buckets. Cell value (0..1) drives accent opacity;
// NO smoothing/interpolation (that would paint resolution the data doesn't have). Each pull is
// framed to its OWN arena, so the picture is self-scaled — never implies a shared floor with
// another pull.
function Heatmap({ d }) {
  const { gridW, gridH, cells, totalSamples, arenaW, arenaH } = d
  const W = 420
  const aspect = arenaW > 0 && arenaH > 0 ? arenaH / arenaW : gridH / gridW
  const H = Math.round(W * Math.min(Math.max(aspect, 0.35), 1.2))
  const thin = totalSamples < THIN_SAMPLES
  return (
    <div className="heatmap">
      <svg width={W} height={H} viewBox={`0 0 ${gridW} ${gridH}`} preserveAspectRatio="none" className="hm-svg">
        <rect x="0" y="0" width={gridW} height={gridH} fill="#0a0b0e" />
        {cells.map((v, i) => {
          if (!v || v <= 0) return null
          const gx = i % gridW
          const gy = Math.floor(i / gridW)
          return <rect key={i} x={gx} y={gy} width="1" height="1" fill="var(--accent)" opacity={0.12 + 0.88 * v} />
        })}
      </svg>
      <div className="hm-legend muted small">
        <span><span className="hm-swatch lo" /> less time</span>
        <span><span className="hm-swatch hi" /> most time</span>
        <span className="hm-meta">
          ≈{totalSamples.toLocaleString()} position readings · framed to this pull{thin ? ' · thin data' : ''}
        </span>
      </div>
    </div>
  )
}

// Career kill-vs-wipe. One-sided is the common case (a boss only wiped, or only killed) — render
// the populated side honestly rather than a column of dashes. N=1 is shown as "1 attempt", never
// dressed as an average.
function WkDelta({ wk }) {
  const k = wk.killCount || 0
  const w = wk.wipeCount || 0
  const rows = wk.rows || []
  const showKill = k > 0
  const showWipe = w > 0

  if (!showKill && !showWipe) return <p className="muted small">No attempts recorded.</p>

  return (
    <div className="wk">
      <div className="wk-head">
        <span className="wk-rowlabel" />
        {showKill && <span className={`wk-col kill ${k === 1 ? 'one' : ''}`}>{k} kill{k === 1 ? '' : 's'}{k === 1 ? ' (1 attempt)' : ''}</span>}
        {showWipe && <span className={`wk-col wipe ${w === 1 ? 'one' : ''}`}>{w} wipe{w === 1 ? '' : 's'}{w === 1 ? ' (1 attempt)' : ''}</span>}
      </div>
      {rows.map((r) => (
        <div className="wk-row" key={r.label}>
          <span className="wk-rowlabel">{r.label}</span>
          {showKill && <span className="wk-col kill">{fmtVal(r.kill, r.unit)}</span>}
          {showWipe && <span className="wk-col wipe">{fmtVal(r.wipe, r.unit)}</span>}
        </div>
      ))}
      {(!showKill || !showWipe) && (
        <p className="muted small wk-note">
          {showWipe ? 'No kill yet — this is what the wipes looked like.' : 'No wipes recorded — clean kills only.'}
        </p>
      )}
    </div>
  )
}

function pullLabel(p) {
  const hp = p.outcome && p.outcome.toLowerCase() === 'kill' ? 'KILL' : `${(p.endHpPct ?? 0).toFixed(0)}%`
  return `P${p.n} · ${hp} · ${fmtDur(p.durationMs)} · ${p.deaths}d`
}

function fmtVal(v, unit) {
  if (v == null) return '—'
  if (unit === 's') return fmtDur(v * 1000)
  if (unit === '%') return `${v.toFixed(1)}%`
  return Number.isInteger(v) ? `${v}` : v.toFixed(1)
}

function fmtDur(ms) {
  const s = Math.round((ms || 0) / 1000)
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}
