import React, { useState } from 'react'
import SetupTab from './SetupTab.jsx'
import LeopardTab from './LeopardTab.jsx'
import TrendsTab from './TrendsTab.jsx'
import PipelineTab from './PipelineTab.jsx'
import RosterTab from './RosterTab.jsx'

export default function App() {
  const [tab, setTab] = useState('leopard')
  const [parseNonce, setParseNonce] = useState(0)

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

      {tab === 'setup' && <SetupTab onParsed={() => setParseNonce((n) => n + 1)} />}
      {tab === 'roster' && <RosterTab key={parseNonce} />}
      {tab === 'trends' && <TrendsTab key={parseNonce} />}
      {tab === 'pipeline' && <PipelineTab key={parseNonce} onOpenTab={setTab} />}
      {tab === 'leopard' && <LeopardTab key={parseNonce} />}

      <footer className="foot">
        <span>Leopard · a reflection engine</span>
        <span className="muted">grounded in your data, on your machine</span>
      </footer>
    </div>
  )
}
