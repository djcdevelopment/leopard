import React, { useEffect, useMemo, useRef, useState } from 'react'
import { getSignals, getPlayers, getAffinity, getDiff, getCoverage, getSegments, getClassify, getShapeDensity } from './api.js'
import { chatStream } from './provider.js'
import { buildMessages } from './prompt.js'
import { ContextBuilder } from './context.js'
import { getObject } from './knowledge.js'
import { buildContract, estimateTokens, findPullIn } from './contract.js'
import ExplorerTree from './ExplorerTree.jsx'
import ExplorerEditor from './ExplorerEditor.jsx'
import ExplorerOutput from './ExplorerOutput.jsx'
import ExplorerProperties from './ExplorerProperties.jsx'

// The Explorer: the knowledge-library IDE (HomeSite-1997 density, Leopard palette).
// Left: the object tree. Center: investigation + the compiled.context editor + the output
// dock. Right: the Properties inspector for the selected slice. The contract the user
// assembles compiles to slice XML (contract.js) and feeds the same ask path AskPanel uses —
// display==send stays structural via ContextBuilder.

const STAGES = ['Lex', 'Parse', 'Segment', 'Replay', 'Reconcile', 'Derived']

export default function ExplorerTab({ night, hasParsed, provider, model }) {
  // Fetched per-night artifacts (one fetch each per night; null = 404 = pre-artifact parse).
  const [art, setArt] = useState({ signals: null, players: null, affinity: null, coverage: null, segments: null, classify: null, shape: null, loaded: false })
  const [pullId, setPullId] = useState('')
  const [contract, setContract] = useState([]) // [{ objectId, slice:{...}, extra:{comparePullId} }]
  const [selectedId, setSelectedId] = useState(null)
  const [diff, setDiff] = useState(null)
  const [question, setQuestion] = useState('')
  const [answer, setAnswer] = useState('')
  const [busy, setBusy] = useState(false)
  const [stats, setStats] = useState(null)
  const [request, setRequest] = useState(null)
  const [folded, setFolded] = useState(true)
  const abortRef = useRef(null)

  useEffect(() => () => abortRef.current?.abort(), [])

  // One fetch fan-out per night. The payloads double as availability probes AND the data
  // for previews + slice serialization — no duplicate requests.
  useEffect(() => {
    if (!night) return
    let alive = true
    setArt({ signals: null, players: null, affinity: null, coverage: null, segments: null, classify: null, shape: null, loaded: false })
    setDiff(null)
    setAnswer('')
    setStats(null)
    setRequest(null)
    Promise.all([
      getSignals(night).catch(() => null),
      getPlayers(night).catch(() => null),
      getAffinity(night).catch(() => null),
      getCoverage(night).catch(() => null),
      getSegments(night).catch(() => null),
      getClassify(night).catch(() => null),
      getShapeDensity(night).catch(() => null),
    ]).then(([signals, players, affinity, coverage, segments, classify, shape]) => {
      if (alive) setArt({ signals, players, affinity, coverage, segments, classify, shape, loaded: true })
    })
    return () => { alive = false }
  }, [night])

  // Flattened pull list (the topbar picker), from the signals artifact.
  const pulls = useMemo(() => {
    const out = []
    for (const enc of art.signals?.encounters || []) {
      for (const p of enc.pulls || []) {
        out.push({
          pullId: p.pullId, n: p.n, outcome: p.outcome,
          encounterId: enc.encounterId, encounterName: enc.encounterName, difficulty: enc.difficulty,
          durationSec: p.signals?.durationSec ?? null,
        })
      }
    }
    return out
  }, [art.signals])

  // Keep the selection valid as nights/pulls change.
  useEffect(() => {
    setPullId((cur) => (cur && pulls.some((p) => p.pullId === cur) ? cur : pulls[pulls.length - 1]?.pullId || ''))
  }, [pulls])

  const pull = pulls.find((p) => p.pullId === pullId) || null
  const comparePulls = useMemo(
    () => pulls.filter((p) => p.encounterId === pull?.encounterId && p.pullId !== pull?.pullId),
    [pulls, pull])

  const diffEntry = contract.find((s) => s.objectId === 'diff.pulls@v1')
  const comparePullId = diffEntry?.extra?.comparePullId || ''

  // Fetch the diff whenever the (compare, current) pair is complete. Left = the compare
  // pull (the baseline), right = the selected pull — "this pull vs that one".
  useEffect(() => {
    if (!night || !pullId || !comparePullId) { setDiff(null); return }
    let alive = true
    getDiff(night, comparePullId, pullId)
      .then((d) => { if (alive) setDiff(d) })
      .catch(() => { if (alive) setDiff(null) })
    return () => { alive = false }
  }, [night, pullId, comparePullId])

  const availability = {
    signals: !!art.signals,
    players: !!art.players,
    affinity: !!art.affinity,
    diff: !!art.signals, // diff reads the same parse vintage's caches
    coverage: !!art.coverage,
    segments: !!art.segments,
    classify: !!art.classify,
    shape: !!art.shape,
    meters: !!art.affinity, // meters ride inside the affinity payload
  }

  const nightData = {
    signals: art.signals, players: art.players, affinity: art.affinity, diff,
    coverage: art.coverage, segments: art.segments, classify: art.classify, shape: art.shape,
  }

  const compiled = useMemo(() => {
    if (!pull) return null
    return buildContract({
      pull: { pullId: pull.pullId, n: pull.n, outcome: pull.outcome },
      encounterName: pull.encounterName,
      difficulty: pull.difficulty,
      durationMs: pull.durationSec != null ? pull.durationSec * 1000 : '',
      slices: contract,
      nightData,
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pull, contract, art, diff])

  const scopeLabel = pull
    ? `Pull ${pull.n} on ${pull.encounterName} (${pull.difficulty}) — composed context (${contract.length} slice${contract.length === 1 ? '' : 's'})`
    : 'this raid night'

  const ctx = useMemo(() => {
    if (!compiled || contract.length === 0) return null
    return new ContextBuilder().setText(compiled.xml, scopeLabel, contract.length).freeze()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [compiled, scopeLabel, contract.length])

  // ── contract mutations ────────────────────────────────────────────────────
  function addObject(id) {
    const entry = getObject(id)
    if (!entry || entry.status !== 'live' || contract.some((s) => s.objectId === id)) return
    const extra = id === 'diff.pulls@v1'
      ? { comparePullId: comparePulls[comparePulls.length - 1]?.pullId || '' }
      : undefined
    setContract((c) => [...c, { objectId: id, slice: { ...entry.sliceDefaults }, extra }])
    setSelectedId(id)
  }
  function removeObject(id) {
    setContract((c) => c.filter((s) => s.objectId !== id))
    setSelectedId((cur) => (cur === id ? null : cur))
  }
  function changeSlice(id, key, value) {
    setContract((c) => c.map((s) => (s.objectId === id ? { ...s, slice: { ...s.slice, [key]: value } } : s)))
  }
  function changeCompare(comparePullId) {
    setContract((c) => c.map((s) => (s.objectId === 'diff.pulls@v1' ? { ...s, extra: { comparePullId } } : s)))
  }

  // ── investigation ─────────────────────────────────────────────────────────
  const canRun = provider?.status === 'ready' && !!ctx && !busy && question.trim().length > 0
  async function runInvestigation() {
    if (!canRun) return
    const q = question.trim()
    setAnswer('')
    setStats(null)
    setBusy(true)
    abortRef.current?.abort()
    const ctrl = new AbortController()
    abortRef.current = ctrl
    const messages = buildMessages(q, ctx.serialize(), ctx.scopeLabel)
    setRequest({ model, messages })
    const t0 = performance.now()
    let full = ''
    try {
      full = await chatStream({
        model, messages, signal: ctrl.signal,
        onToken: (t) => setAnswer((a) => a + t),
      })
    } catch (e) {
      if (e?.name !== 'AbortError') setAnswer(`(error talking to the model: ${e?.message || e})`)
    } finally {
      setBusy(false)
      setStats({ model, latencyMs: performance.now() - t0, completionTok: estimateTokens(full) })
    }
  }

  if (!hasParsed) {
    return <p className="muted">No parsed raids yet — go to <b>Setup / Configuration</b>, pick a night, and click <b>PARSE</b>.</p>
  }
  if (art.loaded && !art.signals) {
    return <p className="muted">This night was parsed before the Explorer's artifacts existed — re-parse it in <b>Setup</b> to light up the knowledge library.</p>
  }

  const selectedEntry = selectedId ? getObject(selectedId) : null
  const selectedItem = contract.find((s) => s.objectId === selectedId) || null

  return (
    <div className="explorer-shell">
      <div className="ex-topbar">
        <label className="ex-pullpick">
          <select value={pullId} onChange={(e) => setPullId(e.target.value)}>
            {pulls.map((p) => (
              <option key={p.pullId} value={p.pullId}>
                Pull {p.n} · {p.encounterName} · {p.outcome}
              </option>
            ))}
          </select>
        </label>
        <span className="ex-stages mono">
          {STAGES.map((s) => <span className="ex-stage" key={s}>{s} <span className="ex-check">✓</span></span>)}
        </span>
      </div>

      <div className="explorer">
        <ExplorerTree
          availability={availability}
          contract={contract}
          selectedId={selectedId}
          onAdd={addObject}
          onRemove={removeObject}
          onSelect={setSelectedId}
          onSeed={setQuestion}
        />

        <main className="ex-center">
          <section className="ex-invest">
            <div className="ex-invest-head">Investigation</div>
            <input
              className="ex-question"
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') runInvestigation() }}
              placeholder="Ask a question of the composed context…"
            />
            <button className="ex-run" disabled={!canRun} onClick={runInvestigation} title={provider?.status !== 'ready' ? 'No local model detected' : !ctx ? 'Add at least one knowledge object to the contract' : ''}>
              {busy ? '… running' : '▶ Run investigation'}
            </button>
          </section>

          {compiled && (
            <ExplorerEditor
              xml={compiled.xml}
              sliceCount={compiled.sliceItems.length}
              totalTok={compiled.totalTok}
              coverage={compiled.coverage}
              folded={folded}
              onToggleFold={() => setFolded((f) => !f)}
            />
          )}

          <ExplorerOutput
            question={stats || busy ? question : ''}
            answer={answer}
            busy={busy}
            stats={stats}
            sliceItems={compiled?.sliceItems || []}
            digest={compiled?.digest}
            request={request}
          />
        </main>

        <ExplorerProperties
          entry={selectedEntry}
          item={selectedItem}
          availability={availability}
          nightData={nightData}
          pull={pull}
          comparePulls={comparePulls}
          onSliceChange={changeSlice}
          onCompareChange={changeCompare}
          onRemove={removeObject}
        />
      </div>
    </div>
  )
}
