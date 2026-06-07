import React, { useEffect, useState } from 'react'
import { getTrends } from './api.js'

// Coherence signals the engine computes per pull. Context first (boss %, deaths), then the
// raid-coordination signals that are the Trends story. Rows with no data anywhere are hidden.
const METRICS = [
  { key: 'bossEndPctHp', label: 'Boss % HP at end', fmt: (v) => `${v.toFixed(1)}%` },
  { key: 'deaths', label: 'Deaths', fmt: (v) => `${v}` },
  { key: 'followershipMean', label: 'Followership', fmt: (v) => v.toFixed(3) },
  { key: 'entropyMean', label: 'Entropy (spread)', fmt: (v) => v.toFixed(3) },
  { key: 'peakSpeed', label: 'Peak speed (yd/s)', fmt: (v) => v.toFixed(1) },
]

// The selectable recent-window sizes — must match TrendsArtifact.Windows on the server.
const WINDOW_SIZES = [4, 6, 8, 10]

export default function TrendsTab({ night, hasParsed }) {
  const [data, setData] = useState(null)
  const [encId, setEncId] = useState('')
  const [status, setStatus] = useState('') // '' | 'loading' | 'unparsed'
  const [winSize, setWinSize] = useState(6)

  // Re-read trends whenever the shared night selection changes.
  useEffect(() => {
    setData(null)
    setEncId('')
    if (!night) { setStatus(''); return }
    setStatus('loading')
    let alive = true
    getTrends(night)
      .then((t) => {
        if (!alive) return
        if (!t) { setStatus('unparsed'); return }
        setData(t)
        setStatus('')
        const encs = t.encounters || []
        // The in-progress boss is the trend-interesting one; fall back to the first.
        const def = encs.find((e) => e.inProgress) || encs[0]
        if (def) setEncId(def.encounterId)
      })
      .catch(() => { if (alive) setStatus('unparsed') })
    return () => { alive = false }
  }, [night])

  const encounters = data?.encounters || []
  const enc = encounters.find((e) => e.encounterId === encId) || null

  if (!hasParsed) {
    return (
      <div className="trends">
        <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, pick a night, and click <b>PARSE</b>.</p>
      </div>
    )
  }

  return (
    <div className="trends">
      {encounters.length > 0 && (
        <div className="picker">
          <label>Boss:&nbsp;
            <select value={encId} onChange={(e) => setEncId(e.target.value)}>
              {encounters.map((e) => (
                <option key={e.encounterId} value={e.encounterId}>
                  {e.encounterName}{e.inProgress ? ' · in progress' : e.kills > 0 ? ' · killed' : ''} ({e.pullCount} pull{e.pullCount === 1 ? '' : 's'})
                </option>
              ))}
            </select>
          </label>
        </div>
      )}

      {status === 'loading' && <p className="muted">Reading trends…</p>}
      {status === 'unparsed' && (
        <p className="muted">This night was parsed before Trends existed — re-parse it in <b>Setup</b> to see its trends.</p>
      )}
      {enc && <EncounterTrends enc={enc} winSize={winSize} setWinSize={setWinSize} />}
    </div>
  )
}

function EncounterTrends({ enc, winSize, setWinSize }) {
  // New artifact: windows/coherences keyed by size. Legacy artifact (parsed before selectable
  // windows): singular window/coherence fixed at n=6.
  const hasMulti = enc.windows && typeof enc.windows === 'object'
  const window = hasMulti ? (enc.windows[winSize] || enc.windows[6]) : enc.window
  const coherence = hasMulti ? (enc.coherences?.[winSize] ?? enc.coherences?.[6]) : enc.coherence

  const rows = window?.ruleRows || []
  const points = coherence?.points || []
  const n = window?.windowSize || 0

  return (
    <>
      {hasMulti ? (
        <div className="wintoggle">
          <span className="muted small">Window:</span>
          {WINDOW_SIZES.map((s) => (
            <button key={s} className={s === winSize ? 'active' : ''} onClick={() => setWinSize(s)}>{s}</button>
          ))}
          <span className="muted small">recent pulls</span>
        </div>
      ) : (
        <p className="muted small">
          Parsed before selectable windows — re-parse in <b>Setup</b> to compare 4 / 6 / 8 / 10 pulls.
        </p>
      )}

      <p className="muted small">
        Recent window: last {n} pull{n === 1 ? '' : 's'}
        {enc.pullCount > n ? `, compared with the ${n} before` : ''}. Every figure is computed by
        the parser — nothing is inferred.
      </p>

      <div className="rulerows">
        {rows.map((r) => (
          <div className="rulerow" key={r.label}>
            <span className="rl-label">{r.label}</span>
            <span className="rl-value">{r.value}</span>
            <span className={`rl-delta ${r.dir}`}>{deltaGlyph(r.dir)} {r.delta}</span>
          </div>
        ))}
      </div>

      {points.length > 0 && (
        <div className="coherence">
          <h3>Across the window</h3>
          <PullAxis points={points} />
          {METRICS.map((m) => {
            const series = points.map((p) => p[m.key])
            if (!series.some((v) => typeof v === 'number' && isFinite(v))) return null
            return <MetricRow key={m.key} label={m.label} series={series} fmt={m.fmt} />
          })}
          <p className="muted small">
            Followership = how together the raid moves · Entropy = how spread their movement is ·
            Peak speed = fastest player movement, in yards/sec. Blank when this night has no replay data.
          </p>
        </div>
      )}
    </>
  )
}

function PullAxis({ points }) {
  return (
    <div className="pullaxis">
      <span className="pa-label muted">Pulls</span>
      {points.map((p) => (
        <span
          key={p.pullId}
          className={`pulltick ${p.outcome === 'kill' ? 'kill' : 'wipe'}`}
          title={`Pull ${p.pullN} · ${p.outcome} · ${p.deaths} deaths`}
        >
          {p.pullN}
        </span>
      ))}
    </div>
  )
}

function MetricRow({ label, series, fmt }) {
  const last = [...series].reverse().find((v) => typeof v === 'number' && isFinite(v))
  return (
    <div className="metric">
      <span className="m-label">{label}</span>
      <Sparkline series={series} />
      <span className="m-last">{typeof last === 'number' && isFinite(last) ? fmt(last) : '—'}</span>
    </div>
  )
}

// Inline-SVG sparkline — zero deps, matching the repo's React-only footprint. Nulls (pulls
// with no replay signal) break the line rather than reading as zero.
function Sparkline({ series }) {
  const W = 200, H = 30, pad = 4
  const vals = series.map((v) => (typeof v === 'number' && isFinite(v) ? v : null))
  const finite = vals.filter((v) => v !== null)
  if (finite.length === 0) return <svg className="spark" width={W} height={H} />
  const min = Math.min(...finite)
  const max = Math.max(...finite)
  const span = max - min || 1
  const n = vals.length
  const x = (i) => (n === 1 ? W / 2 : pad + (i * (W - 2 * pad)) / (n - 1))
  const y = (v) => H - pad - ((v - min) / span) * (H - 2 * pad)
  const pts = vals.map((v, i) => (v === null ? null : `${x(i).toFixed(1)},${y(v).toFixed(1)}`)).filter(Boolean)
  return (
    <svg className="spark" width={W} height={H} viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none">
      <polyline points={pts.join(' ')} fill="none" stroke="var(--accent)" strokeWidth="1.5" />
      {vals.map((v, i) => (v === null ? null : <circle key={i} cx={x(i)} cy={y(v)} r="2" fill="var(--accent)" />))}
    </svg>
  )
}

// Sentiment glyph — up = good, down = bad — independent of whether the raw number rose or fell
// (the engine already decided direction, e.g. fewer deaths is "better").
function deltaGlyph(dir) {
  if (dir === 'better') return '▲'
  if (dir === 'worse') return '▼'
  return '–'
}
