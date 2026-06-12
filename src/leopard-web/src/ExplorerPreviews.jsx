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

export function CoveragePreview({ coverage }) {
  const quality = coverage?.seconds?.quality || []
  if (quality.length === 0) return <p className="muted small">No coverage quality model for this pull.</p>
  const s = coverage.summary
  const snaps = s?.snappingPoints || []
  const w = 168
  const h = 40
  const dx = quality.length > 1 ? w / (quality.length - 1) : 0
  return (
    <div className="ex-preview">
      <svg viewBox={`0 0 ${w} ${h}`} width={w} height={h} className="ex-covchart">
        {/* quality is 0–100 by construction — fixed scale, not auto-fit */}
        <path
          d={quality.map((v, i) => `${i ? 'L' : 'M'}${(i * dx).toFixed(1)},${(h - (v / 100) * (h - 2) - 1).toFixed(1)}`).join('')}
          fill="none" stroke="var(--accent)" strokeWidth="1.2"
        />
        {snaps.map((sn, k) => {
          const sec = sn.timeMs / 1000
          const x = quality.length > 1 ? Math.min(w, (sec / (quality.length - 1)) * w) : 0
          return <text key={k} x={x.toFixed(1)} y={h - 1} fontSize="7" fill="var(--accent)" textAnchor="middle">▼</text>
        })}
      </svg>
      <p className="muted small">
        quality avg {Math.round(s.avgQualityScore)} / min {Math.round(s.minQualityScore)} · raid avg {Math.round(s.avgRaidPct)}%
        {snaps.length > 0 ? ` · ${snaps.length} snap${snaps.length === 1 ? '' : 's'}` : ' · no snaps'}
      </p>
    </div>
  )
}

const FORMATION_OPACITY = { stacked: 0.9, split: 0.5, dispersed: 0.2 }

export function SegmentsPreview({ pull }) {
  const segs = pull?.segments || []
  if (segs.length === 0) return <p className="muted small">No formation segments for this pull.</p>
  const total = segs[segs.length - 1].endMs
  return (
    <div className="ex-preview">
      <svg viewBox="0 0 168 16" width="168" height="16" className="ex-segstrip">
        {segs.map((s, i) => (
          <rect key={i} x={(s.startMs / total) * 168} y="2" width={Math.max(1, ((s.endMs - s.startMs) / total) * 168)} height="12"
            fill="var(--accent)" fillOpacity={FORMATION_OPACITY[s.formation] ?? 0.4}>
            <title>{`${s.formation} ${Math.round(s.startMs / 1000)}–${Math.round(s.endMs / 1000)}s (${Math.round(s.medianPairwiseDistanceYd)}yd)`}</title>
          </rect>
        ))}
      </svg>
      <p className="muted small">{pull.phases || segs.map((s) => s.formation).join(' → ')}</p>
    </div>
  )
}

export function ClassifyPreview({ pull }) {
  if (!pull) return <p className="muted small">No classification for this pull.</p>
  const cls = pull.classification
  if (!cls) return <p className="muted small">Not classified — {pull.reason || 'no verdict'}.</p>
  const label = cls.kind === 'called-wipe' ? `CALLED WIPE · ${cls.calledWipePattern}` : `${cls.kind.toUpperCase()} collapse`
  return (
    <div className="ex-preview">
      <p className="small"><b>{label}</b> <span className="ex-badge trimmed">{cls.confidence}</span></p>
      {cls.kind !== 'called-wipe' && (
        <>
          <p className="muted small">onset ~{Math.round(cls.inflectionMs / 1000)}s{cls.affected?.length > 0 && cls.kind !== 'systemic' ? ` · ${cls.affected.slice(0, 4).join(', ')}` : ''}</p>
          {(cls.evidence || []).slice(0, 3).map((e) => <p className="muted small" key={e.signalId}>· {e.reason}</p>)}
          {cls.coveragePattern && <p className="muted small">coverage: {cls.coveragePattern}{cls.offender ? ` (${cls.offender.displayName})` : ''}</p>}
        </>
      )}
    </div>
  )
}

export function MetersPreview({ meters }) {
  if (!meters || meters.length === 0) return <p className="muted small">No movement meters this night.</p>
  const ranked = [...meters].sort((a, b) => b.totalDistanceYd - a.totalDistanceYd)
  const max = ranked[0].totalDistanceYd || 1
  return (
    <div className="ex-preview">
      {ranked.slice(0, 8).map((m) => (
        <div className="ex-meter-row" key={m.participantId}>
          <span className="ex-meter-name mono">{m.displayName}</span>
          <span className="ex-meter-bar"><span style={{ width: `${(m.totalDistanceYd / max) * 100}%` }} /></span>
          <span className="ex-meter-val mono">{Math.round(m.totalDistanceYd)}yd</span>
        </div>
      ))}
      {ranked.length > 8 && <p className="muted small">+{ranked.length - 8} more</p>}
    </div>
  )
}

export function ShapePreview({ density }) {
  if (!density || !(density.cells || []).length) return <p className="muted small">No density grid for this pull.</p>
  const { gridW, gridH, cells } = density
  const cell = Math.max(2, Math.min(8, Math.floor(168 / gridW)))
  return (
    <div className="ex-preview">
      <svg width={gridW * cell} height={gridH * cell} className="ex-heat">
        {cells.map((v, i) => v > 0 && (
          <rect key={i} x={(i % gridW) * cell} y={Math.floor(i / gridW) * cell} width={cell} height={cell}
            fill="var(--accent)" fillOpacity={Math.max(0.06, v)} />
        ))}
      </svg>
      <p className="muted small">{density.totalSamples} samples · arena ~{Math.round(density.arenaW)}×{Math.round(density.arenaH)} yd</p>
    </div>
  )
}

export function NightArcPreview({ night, encounterName, pullN }) {
  const enc = (night?.encounters || []).find((e) => e.name === encounterName)
  if (!enc) return <p className="muted small">No night arc for this boss — re-parse in Setup.</p>
  const deaths = enc.deathsPerPull || []
  const max = Math.max(1, ...deaths)
  const barW = Math.max(3, Math.min(14, Math.floor(168 / Math.max(1, deaths.length))))
  return (
    <div className="ex-preview">
      <svg width={deaths.length * barW} height="36" className="ex-arcbars">
        {deaths.map((d, i) => (
          <rect key={i} x={i * barW} y={36 - (d / max) * 32 - 2} width={barW - 1} height={(d / max) * 32 + 2}
            fill="var(--accent)" fillOpacity={i + 1 === pullN ? 1 : 0.35}>
            <title>{`pull ${i + 1}: ${d} deaths${i + 1 === pullN ? ' (selected)' : ''}`}</title>
          </rect>
        ))}
      </svg>
      <p className="muted small">
        deaths per pull · selected pull highlighted{enc.killed ? ' · KILLED this night' : enc.deathTrend ? ` · ${enc.deathTrend}` : ''}
      </p>
    </div>
  )
}

export function TrendPreview({ trends, encounterName }) {
  const enc = (trends?.encounters || []).find((e) => e.encounterName === encounterName)
  if (!enc) return <p className="muted small">No trend window for this boss — re-parse in Setup.</p>
  const win = (enc.windows && (enc.windows[enc.defaultWindow] || enc.windows[6])) || enc.window
  const rows = win?.ruleRows || []
  if (rows.length === 0) return <p className="muted small">No rule rows in the window.</p>
  return (
    <div className="ex-preview">
      <p className="muted small">last {win.windowSize} pulls</p>
      {rows.map((r) => (
        <div className="ex-diff-row" key={r.label}>
          <span className="ex-diff-label">{r.label}</span>
          <span className="mono">{String(r.value)} <span className={`ex-delta ${r.dir === 'better' ? 'better' : r.dir === 'worse' ? 'worse' : 'flat'}`}>{r.delta}</span></span>
        </div>
      ))}
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
