import React, { useEffect, useState } from 'react'
import { getCareer } from './api.js'

const fmt = (n) => (typeof n === 'number' ? n.toLocaleString() : n)

function dur(ms) {
  const s = Math.round((ms || 0) / 1000)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}
function dateOf(iso) {
  if (!iso) return '—'
  const d = new Date(iso)
  return isNaN(d.getTime()) ? '—' : d.toLocaleDateString()
}

const DIR = {
  improving: { label: 'improving', cls: 'better' },
  slipping: { label: 'slipping', cls: 'worse' },
  steady: { label: 'steady', cls: 'flat' },
  new: { label: 'new', cls: 'flat' },
}

export default function RosterTab() {
  const [bosses, setBosses] = useState(null)
  const [open, setOpen] = useState('')
  const [err, setErr] = useState('')

  useEffect(() => {
    getCareer()
      .then((d) => setBosses(d.bosses || []))
      .catch((e) => setErr(e?.message || String(e)))
  }, [])

  if (err) return <div className="roster"><p className="err">Couldn't load the roster: {err}</p></div>
  if (!bosses) return <div className="roster"><p className="muted">Reading every night…</p></div>
  if (bosses.length === 0) {
    return (
      <div className="roster">
        <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, parse some nights, and the Roster fans in across all of them.</p>
      </div>
    )
  }

  return (
    <div className="roster">
      <p className="muted small">
        Every boss you've pulled, all-time, across every parsed night. In-progress bosses first,
        closest to a kill on top. <span className="dim">Heroic and Mythic are separate careers.</span>
      </p>

      <div className="rosterwall">
        <div className="rw-head">
          <span>Boss</span>
          <span className="num">Attempts</span>
          <span className="num">Kills</span>
          <span className="num">Best</span>
          <span>Trend</span>
          <span>Progress arc</span>
        </div>
        {bosses.map((b) => (
          <BossRow key={b.careerId} b={b} open={open === b.careerId} onToggle={() => setOpen(open === b.careerId ? '' : b.careerId)} />
        ))}
      </div>

      <p className="muted small dim">
        Progress arc = boss % HP remaining at the end of each attempt, oldest → newest. Lower is
        closer to a kill; the floor (gold) is a kill at 0%.
      </p>
    </div>
  )
}

function BossRow({ b, open, onToggle }) {
  const dir = DIR[b.direction] || DIR.new
  const best = b.killed ? 'killed' : typeof b.bestPct === 'number' ? `${b.bestPct.toFixed(1)}%` : '—'
  return (
    <>
      <button className={`rw-row ${open ? 'open' : ''}`} onClick={onToggle}>
        <span className="rw-boss"><b>{b.name}</b> <span className={`diffbadge ${(b.difficulty || '').toLowerCase()}`}>{b.difficulty}</span></span>
        <span className="num">{fmt(b.attempts)}</span>
        <span className={`num ${b.kills > 0 ? 'ok' : ''}`}>{b.kills}</span>
        <span className={`num ${b.killed ? 'accent' : ''}`}>{best}</span>
        <span><span className={`rl-delta ${dir.cls}`}>{dir.label}</span></span>
        <Arc arc={b.arc} />
      </button>
      {open && (
        <div className="rw-drill">
          <div className="rw-stats">
            <Stat label="First pulled" v={dateOf(b.firstSeen)} />
            <Stat label="Last pulled" v={dateOf(b.lastSeen)} />
            <Stat label="Total time" v={dur(b.totalTimeMs)} />
            <Stat label="Attempts" v={fmt(b.attempts)} />
            <Stat label={b.killed ? 'Kills' : 'Best %HP'} v={b.killed ? fmt(b.kills) : best} />
          </div>
          <Arc arc={b.arc} big />
        </div>
      )}
    </>
  )
}

function Stat({ label, v }) {
  return <div className="rw-stat"><span className="muted">{label}</span><b>{v}</b></div>
}

// Progress arc on a fixed 0..100% domain so bosses read consistently. 100% (fight start) at
// the top, 0% (a kill) at the floor — so a line descending left→right means progress.
function Arc({ arc, big }) {
  const W = big ? 640 : 200
  const H = big ? 84 : 26
  const pad = 4
  const vals = (arc || []).map((v) => (typeof v === 'number' && isFinite(v) ? v : null))
  if (vals.every((v) => v === null)) return <svg className="spark" width={W} height={H} />
  const n = vals.length
  const x = (i) => (n === 1 ? W / 2 : pad + (i * (W - 2 * pad)) / (n - 1))
  const y = (v) => pad + ((100 - v) / 100) * (H - 2 * pad)
  const pts = vals.map((v, i) => (v === null ? null : `${x(i).toFixed(1)},${y(v).toFixed(1)}`)).filter(Boolean)
  return (
    <svg className="spark arc" width={W} height={H} viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none">
      {/* kill floor */}
      <line x1={pad} y1={y(0)} x2={W - pad} y2={y(0)} stroke="var(--accent)" strokeWidth="0.5" strokeDasharray="2 3" opacity="0.5" />
      <polyline points={pts.join(' ')} fill="none" stroke="var(--accent)" strokeWidth="1.5" />
      {vals.map((v, i) => (v === null ? null : <circle key={i} cx={x(i)} cy={y(v)} r={v === 0 ? 2.6 : 1.8} fill={v === 0 ? 'var(--accent)' : '#9aa0ad'} />))}
    </svg>
  )
}
