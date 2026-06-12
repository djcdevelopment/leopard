import React from 'react'
import { findPullIn } from './contract.js'
import {
  SignalsPreview, PlayersPreview, AffinityPreview, DiffPreview,
  CoveragePreview, SegmentsPreview, ClassifyPreview, MetersPreview, ShapePreview,
  NightArcPreview, TrendPreview,
} from './ExplorerPreviews.jsx'

// The right rail: Properties — shape the selected slice. Metadata rows come straight from
// the knowledge registry; the SLICE dropdowns are bound to the contract entry (every change
// recompiles the contract, so the digest and tok= attrs visibly move); Preview renders the
// RaidUI-style viz for the module; "In contract — remove" really removes.

const ANNOTATION_ONLY = 'recorded in the XML as an annotation — the compiler does not branch on it yet'

function MetaRow({ label, children }) {
  return (
    <div className="ex-meta">
      <span className="ex-meta-label">{label}</span>
      <span className="ex-meta-value">{children}</span>
    </div>
  )
}

// Which slice keys the compiler actually branches on, per object (kept in sync with
// contract.js's serializers; everything else is an honest annotation).
const REAL_KEYS = {
  'signals.pull@v1': ['rep', 'agg', 'time'],
  'players.pull@v1': ['scope'],
  'affinity.night@v1': ['rep'],
  'diff.pulls@v1': [],
  'coverage.timeline@v1': ['rep'],
  'segments.formation@v1': [],
  'classify.wipe@v1': ['rep'],
  'meters.movement@v1': ['scope', 'agg'],
  'shape.density@v1': [],
  'progression.encounter@v1': [],
  'trend.window@v1': ['rep'],
}

export default function ExplorerProperties({
  entry, item, availability, nightData, pull, comparePulls,
  onSliceChange, onCompareChange, onRemove,
}) {
  if (!entry) {
    return (
      <aside className="ex-props">
        <div className="ex-props-head"><b>Properties</b><span className="muted small">shape the selected slice</span></div>
        <p className="muted small">Select a knowledge object in the tree — its provenance, slice controls, and preview land here.</p>
      </aside>
    )
  }

  const live = entry.status === 'live' && availability[entry.api]
  const inContract = !!item

  function preview() {
    if (entry.api === 'signals') return <SignalsPreview pullSignals={findPullIn(nightData.signals, pull?.pullId)?.signals} />
    if (entry.api === 'players') return <PlayersPreview scores={findPullIn(nightData.players, pull?.pullId)?.scores} />
    if (entry.api === 'affinity') return <AffinityPreview affinity={nightData.affinity} />
    if (entry.api === 'diff') return <DiffPreview diff={nightData.diff} />
    if (entry.api === 'coverage') return <CoveragePreview coverage={findPullIn(nightData.coverage, pull?.pullId)?.coverage} />
    if (entry.api === 'segments') return <SegmentsPreview pull={findPullIn(nightData.segments, pull?.pullId)} />
    if (entry.api === 'classify') return <ClassifyPreview pull={findPullIn(nightData.classify, pull?.pullId)} />
    if (entry.api === 'meters') return <MetersPreview meters={nightData.affinity?.meters} />
    if (entry.api === 'shape') return <ShapePreview density={findPullIn(nightData.shape, pull?.pullId)?.density} />
    if (entry.api === 'night') return <NightArcPreview night={nightData.night} encounterName={pull?.encounterName} pullN={pull?.n} />
    if (entry.api === 'trends') return <TrendPreview trends={nightData.trends} encounterName={pull?.encounterName} />
    return <p className="muted small">No preview until this object is wired (phase 2).</p>
  }

  return (
    <aside className="ex-props">
      <div className="ex-props-head">
        <b>Properties</b>
        <span className="muted small">shape the selected slice</span>
      </div>

      <div className="ex-props-title">
        <span className="ex-props-name">{entry.label}</span>
        {live && <span className="ex-badge live">● live</span>}
        {entry.status === 'ghost' && <span className="ex-badge ghosted">planned</span>}
      </div>
      <p className="muted mono small">{entry.id}</p>

      <MetaRow label="Stage">{entry.stage}</MetaRow>
      <MetaRow label="Truth"><span className={`ex-badge ${entry.truth.toLowerCase()}`}>{entry.truth}</span></MetaRow>
      <MetaRow label="Confidence">{entry.confidence}</MetaRow>
      <MetaRow label="Status">{entry.status === 'ghost' ? 'planned' : availability[entry.api] ? 'live' : 're-parse needed'}</MetaRow>
      <MetaRow label="Stream"><span className="ex-badge trimmed">{entry.stream.toUpperCase()}</span></MetaRow>
      <MetaRow label="Prompt fit">{entry.promptFit}</MetaRow>
      <MetaRow label="Visualization fit">{entry.vizFit}</MetaRow>

      {inContract && entry.sliceOptions && (
        <>
          <div className="ex-props-sect">Slice</div>
          {Object.entries(entry.sliceOptions).map(([key, opts]) => {
            const real = (REAL_KEYS[entry.id] || []).includes(key)
            const label = { scope: 'Scope', rep: 'Representation', agg: 'Aggregation', time: 'Time bound' }[key] || key
            return (
              <MetaRow label={label} key={key}>
                <select
                  value={item.slice?.[key] ?? entry.sliceDefaults[key]}
                  onChange={(e) => onSliceChange(entry.id, key, e.target.value)}
                  disabled={opts.length < 2}
                  title={real ? 'Reshapes the slice payload' : ANNOTATION_ONLY}
                >
                  {opts.map((o) => <option key={o} value={o}>{o}</option>)}
                </select>
                {!real && opts.length > 1 && <span className="muted small" title={ANNOTATION_ONLY}> ⓘ</span>}
              </MetaRow>
            )
          })}
          {entry.api === 'diff' && (
            <MetaRow label="Compare with">
              <select value={item.extra?.comparePullId || ''} onChange={(e) => onCompareChange(e.target.value)}>
                <option value="">— pick a pull —</option>
                {comparePulls.map((p) => <option key={p.pullId} value={p.pullId}>Pull {p.n} · {p.outcome}{p.best ? ' · best' : ''}</option>)}
              </select>
            </MetaRow>
          )}
        </>
      )}

      {(entry.builtFrom.length > 0 || entry.feeds.length > 0) && (
        <div className="ex-lineage small">
          {entry.builtFrom.length > 0 && <p><span className="ex-meta-label">Built from</span> {entry.builtFrom.join(', ')}</p>}
          {entry.feeds.length > 0 && <p><span className="ex-meta-label">Feeds</span> {entry.feeds.join(', ')}</p>}
        </div>
      )}

      <details className="ex-disclosure" open={inContract}>
        <summary>Preview</summary>
        {preview()}
      </details>
      <details className="ex-disclosure">
        <summary>Description &amp; stream</summary>
        <p className="small">{entry.description}</p>
      </details>

      {inContract
        ? <button className="ex-contract-btn on" onClick={() => onRemove(entry.id)}>✓ In contract — remove</button>
        : entry.status === 'live'
          ? <p className="muted small">Not in the contract — add it with “+” in the tree.</p>
          : <p className="muted small">Planned: lights up in phase 2.</p>}
    </aside>
  )
}
