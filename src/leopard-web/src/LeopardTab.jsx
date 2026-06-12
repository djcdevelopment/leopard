import React from 'react'
import AskPanel from './AskPanel.jsx'

// The front door: the provider bar + the full zoom-aware Ask. Provider/model are owned by App and
// shared with the Pipeline-terminus Ask. The Ask experience itself lives in AskPanel (reused).
export default function LeopardTab({ provider, model, setModel, night, hasParsed }) {
  return (
    <div className="leopard">
      <ProviderBar provider={provider} model={model} setModel={setModel} />
      <AskPanel provider={provider} model={model} night={night} hasParsed={hasParsed} allowZoom />
    </div>
  )
}

function ProviderBar({ provider, model, setModel }) {
  if (provider.status === 'checking') return <div className="bar checking">Looking for a local model…</div>
  if (provider.status === 'absent')
    return <div className="bar absent">No local model found — start your model provider (Ollama, or the configured endpoint); Leopard will pick it up automatically. Your data never leaves your machine.</div>
  return (
    <div className="bar ready">
      Local model:&nbsp;
      <select value={model} onChange={(e) => setModel(e.target.value)}>
        {provider.models.map((m) => <option key={m} value={m}>{m}</option>)}
      </select>
      &nbsp;· on your machine · nothing leaves your PC
    </div>
  )
}
