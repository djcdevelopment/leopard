import React, { useState } from 'react'
import SetupTab from './SetupTab.jsx'
import LeopardTab from './LeopardTab.jsx'

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
        <button className={tab === 'setup' ? 'active' : ''} onClick={() => setTab('setup')}>Setup / Configuration</button>
      </nav>

      {tab === 'setup'
        ? <SetupTab onParsed={() => setParseNonce((n) => n + 1)} />
        : <LeopardTab key={parseNonce} />}

      <footer className="foot">
        <span>Leopard · a reflection engine</span>
        <span className="muted">grounded in your data, on your machine</span>
      </footer>
    </div>
  )
}
