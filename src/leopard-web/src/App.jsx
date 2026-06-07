import React, { useState, useEffect, useCallback } from 'react'
import SetupTab from './SetupTab.jsx'
import LeopardTab from './LeopardTab.jsx'
import TrendsTab from './TrendsTab.jsx'
import PipelineTab from './PipelineTab.jsx'
import RosterTab from './RosterTab.jsx'
import { getLogs } from './api.js'

// Tabs that reflect a single chosen night share one selection (below). The Roster is
// corpus-wide and Setup has its own log grid, so neither uses the shared picker.
const NIGHT_SCOPED = new Set(['leopard', 'trends', 'pipeline'])

export default function App() {
  const [tab, setTab] = useState('leopard')
  const [parseNonce, setParseNonce] = useState(0)
  const [parsedLogs, setParsedLogs] = useState([])
  const [selectedNight, setSelectedNight] = useState('')

  // Single source of truth for the parsed-night list + the chosen night, shared across the
  // night-scoped tabs. Re-fetched whenever a parse completes (parseNonce bump); the selection
  // is preserved if still valid, else falls back to the newest parsed night.
  useEffect(() => {
    getLogs()
      .then((l) => {
        const parsed = (l.logs || []).filter((x) => x.parsed)
        setParsedLogs(parsed)
        setSelectedNight((cur) =>
          cur && parsed.some((p) => p.name === cur) ? cur : parsed[0]?.name || '')
      })
      .catch(() => {})
  }, [parseNonce])

  // After a parse: refresh the list and jump the shared selection to the just-parsed night.
  const onParsed = useCallback((parsedNames) => {
    setParseNonce((n) => n + 1)
    if (parsedNames?.length) setSelectedNight(parsedNames[0])
  }, [])

  // Setup's "Ask about this night →" hand-off.
  const goToNight = useCallback((name) => {
    if (name) setSelectedNight(name)
    setTab('leopard')
  }, [])

  const showPicker = NIGHT_SCOPED.has(tab) && parsedLogs.length > 0
  const hasParsed = parsedLogs.length > 0

  return (
    <div className="wrap">
      <header>
        <h1>🐆 Leopard</h1>
        <p className="tagline">Ask your own raid — on your own machine.</p>
      </header>

      <nav className="tabs">
        <button className={tab === 'leopard' ? 'active' : ''} onClick={() => setTab('leopard')}>Leopard</button>
        <button className={tab === 'roster' ? 'active' : ''} onClick={() => setTab('roster')}>Roster</button>
        <button className={tab === 'trends' ? 'active' : ''} onClick={() => setTab('trends')}>Trends</button>
        <button className={tab === 'pipeline' ? 'active' : ''} onClick={() => setTab('pipeline')}>Pipeline</button>
        <button className={tab === 'setup' ? 'active' : ''} onClick={() => setTab('setup')}>Setup / Configuration</button>
      </nav>

      {showPicker && (
        <div className="picker">
          <label>Raid night:&nbsp;
            <select value={selectedNight} onChange={(e) => setSelectedNight(e.target.value)}>
              {parsedLogs.map((l) => <option key={l.name} value={l.name}>{l.name} · {l.modified}</option>)}
            </select>
          </label>
        </div>
      )}

      {tab === 'setup' && <SetupTab onParsed={onParsed} onGoToNight={goToNight} />}
      {tab === 'roster' && <RosterTab key={parseNonce} />}
      {tab === 'trends' && <TrendsTab night={selectedNight} hasParsed={hasParsed} />}
      {tab === 'pipeline' && <PipelineTab night={selectedNight} hasParsed={hasParsed} onOpenTab={setTab} />}
      {tab === 'leopard' && <LeopardTab night={selectedNight} hasParsed={hasParsed} />}

      <footer className="foot">
        <span>Leopard · a reflection engine</span>
        <span className="muted">grounded in your data, on your machine</span>
      </footer>
    </div>
  )
}
