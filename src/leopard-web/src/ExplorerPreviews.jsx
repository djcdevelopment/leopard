import React from 'react'

// Pure presentational previews for the Properties panel's "Preview" disclosure — one tiny
// SVG per live knowledge object, echoing how RaidUI originally rendered each module
// (sparkline stack / leaderboard bars / N×N heatmap / metric rows). Prop-fed, no fetching.

// Break a (possibly null-holed) series into M/L path segments, normalized into a w×h box.
function sparkPath(values, w, h) {
  const finite = values.filter((v) => typeof v === 'number' && isFinite(v))
  if (finite.length === 0) return ''
  const min = Math.min(...finite)
  const max = Math.max(...finite)
  const span = max - min || 1
  const dx = values.length > 1 ? w / (values.length - 1) : 0
  let d = ''
  let pen = false
  values.forEach((v, i) => {
    if (typeof v !== 'number' || !isFinite(v)) { pen = false; return }
    const x = (i * dx).toFixed(1)
    const y = (h - ((v - min) / span) * (h - 2) - 1).toFixed(1)
    d += `${pen ? 'L' : 'M'}${x},${y}`
    pen = true
  })
  return d
}

const SIGNAL_ORDER = ['spacing', 'coverage', 'deathsPerSec', 'followership', 'entropy', 'hpVariance']
const SIGNAL_LABEL = {
  spacing: 'spacing', coverage: 'coverage', deathsPerSec: 'deaths/s',
  followership: 'follower', entropy: 'entropy', hpVariance: 'hp-var',
}

export function SignalsPreview({ pullSignals }) {
  if (!pullSignals) return <p className="muted small">No replay-derived signals for this pull.</p>
  return (
    <div className="ex-preview">
      {SIGNAL_ORDER.map((key) => {
        const s = pullSignals.signals?.[key]
        if (!s) return null
        return (
          <div className="ex-spark" key={key}>
            <span className="ex-spark-label mono">{SIGNAL_LABEL[key]}</span>
            <svg viewBox="0 0 120 24" className="ex-spark-svg" preserveAspectRatio="none">
              <path d={sparkPath(s.values, 120, 24)} fill="none" stroke="var(--accent)" strokeWidth="1.2" />
            </svg>
            <span className="ex-spark-val mono">{s.peak ? `${Number(s.peak.value).toFixed(2)}${s.unit ? ` ${s.unit}` : ''} @ ${s.peak.atSec}s` : ''}</span>
          </div>
        )
      })}
      {(pullSignals.snaps || []).length > 0 && (
        <p className="muted small">{pullSignals.snaps.length} snap{pullSignals.snaps.length === 1 ? '' : 's'}: {pullSignals.snaps.map((sn) => `−${sn.dropPct}pp @ ${sn.atSec}s`).join(', ')}</p>
      )}
    </div>
  )
}

export function PlayersPreview({ scores }) {
  if (!scores || scores.length === 0) return <p className="muted small">No player scores for this pull.</p>
  return (
    <div className="ex-preview">
      {scores.slice(0, 8).map((p) => (
        <div className="ex-meter-row" key={p.entityId}>
          <span className="ex-meter-name mono">{p.displayName}</span>
          <span className="ex-meter-bar"><span style={{ width: `${p.compositeScore}%` }} /></span>
          <span className="ex-meter-val mono">{p.compositeScore}</span>
          <span className="ex-arche">{p.archetype}</span>
        </div>
      ))}
      {scores.length > 8 && <p className="muted small">+{scores.length - 8} more</p>}
    </div>
  )
}

export function AffinityPreview({ affinity }) {
  const parts = affinity?.participants || []
  const n = parts.length
  if (n < 2 || !affinity.composite) return <p className="muted small">No movement-affinity structure this night.</p>
  const cell = Math.max(4, Math.min(12, Math.floor(168 / n)))
  return (
    <div className="ex-preview">
      <svg width={n * cell} height={n * cell} className="ex-heat">
        {parts.map((_, i) => parts.map((__, j) => (
          <rect key={`${i}-${j}`} x={j * cell} y={i * cell} width={cell - 1} height={cell - 1}
            fill="var(--accent)" fillOpacity={i === j ? 0.9 : Math.max(0.04, affinity.composite[i * n + j])} />
        )))}
      </svg>
      <p className="muted small">{n} players · {(affinity.groups || []).length} movement groups{affinity.coverageGaps?.gapCount ? ` · ${affinity.coverageGaps.gapCount} coverage gap${affinity.coverageGaps.gapCount === 1 ? '' : 's'}` : ''}</p>
    </div>
  )
}

export function DiffPreview({ diff }) {
  if (!diff) return <p className="muted small">Pick a compare pull to see the diff.</p>
  return (
    <div className="ex-preview">
      <p className="small"><b>{diff.ruleHeadline}</b></p>
      {(diff.metrics || []).map((m) => (
        <div className="ex-diff-row" key={m.id}>
          <span className="ex-diff-label">{m.label}</span>
          {m.wired ? (
            <span className="mono">
              {String(m.l)}{m.unit} → {String(m.r)}{m.unit}
              {m.delta && <span className={`ex-delta ${m.dir}`}> {m.delta}</span>}
            </span>
          ) : (
            <span className="muted small">needs replay frames</span>
          )}
        </div>
      ))}
    </div>
  )
}
