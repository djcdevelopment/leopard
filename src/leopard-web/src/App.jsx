import React, { useState, useEffect, useCallback } from 'react'
import SetupTab from './SetupTab.jsx'
import LeopardTab from './LeopardTab.jsx'
import TrendsTab from './TrendsTab.jsx'
import ShapeTab from './ShapeTab.jsx'
import LiveTab from './LiveTab.jsx'
import PipelineTab from './PipelineTab.jsx'
import RosterTab from './RosterTab.jsx'
import ExplorerTab from './ExplorerTab.jsx'
import { getLogs } from './api.js'
import { detectProvider, loadedModels } from './provider.js'

export default function App() {
  const [tab, setTab] = useState('leopard')
  const [parseNonce, setParseNonce] = useState(0)
  const [parsedLogs, setParsedLogs] = useState([])
  const [selectedNight, setSelectedNight] = useState('')
  // Provider/model are app-level so every Ask surface — the Leopard tab and the Pipeline
  // terminus — shares one selection. Detection RETRIES every 5 s while the provider is
  // absent: a local model server often finishes loading after the app window opens
  // (observed 2026-06-11 with the B70 llama-server), and a one-shot detect would dead-end
  // on a banner until an app restart.
  const [provider, setProvider] = useState({ status: 'checking', models: [] })
  const [model, setModel] = useState('')

  useEffect(() => {
    let alive = true
    let timer = null
    async function detect() {
      const p = await detectProvider()
      if (!alive) return
      setProvider({ status: p.reachable ? 'ready' : 'absent', models: p.models, error: p.error })
      if (p.reachable && p.models?.length) {
        const loaded = await loadedModels()
        if (!alive) return
        const warm = loaded.find((m) => p.models.includes(m))
        const pref = [/b70/i, /mistral/i, /qwen2\.5[:\-]?14b/i, /:14b/i, /qwen2\.5[:\-]?32b/i, /:32b/i, /instruct/i]
        setModel(warm || pref.map((re) => p.models.find((m) => re.test(m))).find(Boolean) || p.models[0])
      } else {
        timer = setTimeout(detect, 5000)
      }
    }
    detect()
    return () => { alive = false; clearTimeout(timer) }
  }, [])

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

  const hasParsed = parsedLogs.length > 0

  return (
    <div className={tab === 'explorer' ? 'wrap wrap-wide' : 'wrap'}>
      <header>
        <h1>🐆 Leopard</h1>
        <p className="tagline">Ask your own raid — on your own machine.</p>
      </header>

      {/* Global raid-night context: one selector above the tabs, not repeated inside each tab.
          Shown whenever any night is parsed; it drives the night-scoped tabs (Leopard / Trends
          / Pipeline). Roster is all-time and Setup picks its own logs, so they ignore it. */}
      {hasParsed && (
        <div className="picker globalpicker">
          <label>Raid night:&nbsp;
            <select value={selectedNight} onChange={(e) => setSelectedNight(e.target.value)}>
              {parsedLogs.map((l) => <option key={l.name} value={l.name}>{l.name} · {l.modified}</option>)}
            </select>
          </label>
        </div>
      )}

      <nav className="tabs">
        <button className={tab === 'leopard' ? 'active' : ''} onClick={() => setTab('leopard')}>Leopard</button>
        <button className={tab === 'roster' ? 'active' : ''} onClick={() => setTab('roster')}>Roster</button>
        <button className={tab === 'trends' ? 'active' : ''} onClick={() => setTab('trends')}>Trends</button>
        <button className={tab === 'shape' ? 'active' : ''} onClick={() => setTab('shape')}>Shape</button>
        <button className={tab === 'live' ? 'active' : ''} onClick={() => setTab('live')}>Live</button>
        <button className={tab === 'pipeline' ? 'active' : ''} onClick={() => setTab('pipeline')}>Pipeline</button>
        <button className={tab === 'explorer' ? 'active' : ''} onClick={() => setTab('explorer')}>Explorer</button>
        <button className={tab === 'setup' ? 'active' : ''} onClick={() => setTab('setup')}>Setup / Configuration</button>
      </nav>

      {tab === 'setup' && <SetupTab onParsed={onParsed} onGoToNight={goToNight} />}
      {tab === 'roster' && <RosterTab key={parseNonce} />}
      {tab === 'trends' && <TrendsTab night={selectedNight} hasParsed={hasParsed} />}
      {tab === 'shape' && <ShapeTab night={selectedNight} hasParsed={hasParsed} />}
      {tab === 'live' && <LiveTab />}
      {tab === 'pipeline' && <PipelineTab night={selectedNight} hasParsed={hasParsed} onOpenTab={setTab} provider={provider} model={model} />}
      {tab === 'explorer' && <ExplorerTab night={selectedNight} hasParsed={hasParsed} provider={provider} model={model} />}
      {tab === 'leopard' && <LeopardTab night={selectedNight} hasParsed={hasParsed} provider={provider} model={model} setModel={setModel} />}

      <footer className="foot">
        <span>Leopard · a reflection engine</span>
        <span className="muted">grounded in your data, on your machine</span>
      </footer>
    </div>
  )
}
