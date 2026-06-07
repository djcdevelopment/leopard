import React, { useEffect, useState } from 'react'
import { getConfig, getLogs, parseLogs, setConfig, pickFolder } from './api.js'

export default function SetupTab({ onParsed, onGoToNight }) {
  const [config, setConfigState] = useState(null)
  const [dirInput, setDirInput] = useState('')
  const [logs, setLogs] = useState(null)
  const [err, setErr] = useState('')
  const [sel, setSel] = useState(() => new Set())
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState('')
  const [lastParsed, setLastParsed] = useState([]) // names parsed in the most recent run — the Ask hand-off

  async function refresh() {
    setErr('')
    try {
      const cfg = await getConfig()
      setConfigState(cfg)
      setDirInput(cfg.logDir)
      setLogs(await getLogs())
    } catch (e) {
      setErr(String(e?.message || e))
    }
  }
  useEffect(() => { refresh() }, [])

  async function saveDir(dir) {
    try { await setConfig(dir); await refresh(); setStatus('Folder saved.') }
    catch (e) { setStatus(`Save failed: ${e?.message || e}`) }
  }
  async function browse() {
    try {
      const res = await pickFolder()
      if (!res.available) { setStatus('Browse works in the desktop app — type/paste the path here instead.'); return }
      if (res.cancelled) return
      setDirInput(res.logDir)
      await refresh()
      setStatus('Folder set.')
    } catch (e) { setStatus(`Browse failed: ${e?.message || e}`) }
  }

  function toggle(name) {
    setSel((s) => { const n = new Set(s); n.has(name) ? n.delete(name) : n.add(name); return n })
  }

  async function parseSelected() {
    if (!sel.size || busy) return
    setBusy(true)
    setStatus(`Parsing ${sel.size} log${sel.size > 1 ? 's' : ''}… (a big raid can take ~15s each)`)
    try {
      const res = await parseLogs([...sel])
      const okNames = res.results.filter((r) => r.ok).map((r) => r.name)
      const failed = res.results.filter((r) => !r.ok)
      setStatus(`Parsed ${okNames.length}/${res.results.length}.` + (failed.length ? ` Failed: ${failed.map((f) => f.name + ' (' + f.error + ')').join(', ')}` : ''))
      setSel(new Set())
      setLastParsed(okNames)
      await refresh()
      onParsed?.(okNames)
    } catch (e) {
      setStatus(`Error: ${e?.message || e}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="setup">
      <h2>Your combat logs</h2>

      <div className="cfg">
        <label>Logs folder:</label>
        <input className="cfgpath" value={dirInput} onChange={(e) => setDirInput(e.target.value)} spellCheck={false} />
        <button onClick={browse}>Browse…</button>
        <button onClick={() => saveDir(dirInput)} disabled={!dirInput || dirInput === config?.logDir}>Save</button>
      </div>

      {err && <p className="err">{err} — is the Leopard host running?</p>}
      {logs && !logs.exists && <p className="err">That folder doesn’t exist. Pick or paste a valid one above.</p>}

      {logs?.logs?.length > 0 && (
        <>
          <div className="gridwrap">
            <table className="grid">
              <thead>
                <tr><th style={{ width: 28 }}></th><th>Log</th><th>Size</th><th>Modified</th><th>Status</th></tr>
              </thead>
              <tbody>
                {logs.logs.map((l) => (
                  <tr key={l.name} className={sel.has(l.name) ? 'sel' : ''} onClick={() => toggle(l.name)}>
                    <td><input type="checkbox" checked={sel.has(l.name)} onChange={() => toggle(l.name)} onClick={(e) => e.stopPropagation()} /></td>
                    <td className="mono">{l.name}</td>
                    <td>{l.sizeMb} MB</td>
                    <td className="muted">{l.modified}</td>
                    <td>{l.parsed ? <span className="ok">● Parsed</span> : <span className="muted">—</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="actions">
            <button disabled={!sel.size || busy} onClick={parseSelected}>{busy ? 'Parsing…' : `PARSE${sel.size ? ' ' + sel.size : ''}`}</button>
            <span className="muted">{status}</span>
          </div>
          {lastParsed.length > 0 && !busy && (
            <div className="actions">
              <button onClick={() => onGoToNight?.(lastParsed[0])}>Ask about this night →</button>
              <span className="muted">{lastParsed.length} night{lastParsed.length > 1 ? 's' : ''} ready — explore in Leopard, Trends, or Pipeline.</span>
            </div>
          )}
        </>
      )}
    </div>
  )
}
