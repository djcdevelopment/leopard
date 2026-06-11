import React, { useState } from 'react'
import { KNOWLEDGE_OBJECTS, CATEGORY_ORDER, EXPLORER_SEEDS } from './knowledge.js'

// The left rail: PROJECT EXPLORER — knowledge library. Search box, filter chips
// (Available / Used N / All), the starred QUESTIONS section, then the categorized
// object tree with availability dots and "+" add-to-contract. Props-only, no fetching.
//
// availability: { [api]: boolean } — whether each live object's artifact loaded for this
// night (false ⇒ amber dot: parsed before the artifact existed, re-parse in Setup).
export default function ExplorerTree({ availability, contract, selectedId, onAdd, onRemove, onSelect, onSeed }) {
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState('all') // 'available' | 'used' | 'all'

  const inContract = (id) => contract.some((s) => s.objectId === id)
  const q = query.trim().toLowerCase()

  const visible = KNOWLEDGE_OBJECTS.filter((o) => {
    if (q && !o.label.toLowerCase().includes(q) && !o.id.toLowerCase().includes(q)) return false
    if (filter === 'available') return o.status === 'live'
    if (filter === 'used') return inContract(o.id)
    return true
  })

  function dotClass(o) {
    if (o.status === 'ghost') return 'ex-dot ghost'
    return availability[o.api] ? 'ex-dot live' : 'ex-dot stale'
  }

  function dotTitle(o) {
    if (o.status === 'ghost') return 'Planned — not wired yet'
    return availability[o.api] ? 'Available for this night' : 'Parsed before this artifact existed — re-parse in Setup'
  }

  return (
    <aside className="ex-tree">
      <div className="ex-tree-head">
        <span className="ex-tree-title">Project explorer</span>
        <span className="muted small">knowledge library</span>
      </div>
      <input
        className="ex-search" type="search" placeholder="Search knowledge…"
        value={query} onChange={(e) => setQuery(e.target.value)}
      />
      <div className="ex-chips">
        <button className={filter === 'available' ? 'on' : ''} onClick={() => setFilter('available')}>Available</button>
        <button className={filter === 'used' ? 'on' : ''} onClick={() => setFilter('used')}>Used {contract.length}</button>
        <button className={filter === 'all' ? 'on' : ''} onClick={() => setFilter('all')}>All</button>
      </div>

      {filter !== 'used' && !q && (
        <div className="ex-cat">
          <div className="ex-cat-head">★ Questions <span className="ex-cat-n">{EXPLORER_SEEDS.length}</span></div>
          {EXPLORER_SEEDS.map((s) => (
            <button className="ex-leaf ex-seed" key={s} onClick={() => onSeed(s)} title="Use as the investigation question">
              <span className="ex-seed-glyph">▸</span> {s}
            </button>
          ))}
        </div>
      )}

      {CATEGORY_ORDER.map((cat) => {
        const objs = visible.filter((o) => o.category === cat)
        if (objs.length === 0) return null
        return (
          <div className="ex-cat" key={cat}>
            <div className="ex-cat-head">{cat.toLowerCase()} <span className="ex-cat-n">{objs.length}</span></div>
            {objs.map((o) => {
              const used = inContract(o.id)
              return (
                <div
                  className={`ex-leaf${o.status === 'ghost' ? ' ghost' : ''}${selectedId === o.id ? ' sel' : ''}`}
                  key={o.id}
                  onClick={() => onSelect(o.id)}
                  title={o.description}
                >
                  <span className={dotClass(o)} title={dotTitle(o)} />
                  <span className="ex-leaf-label">{o.label}</span>
                  {o.badge && <span className="ex-badge raw">{o.badge}</span>}
                  {o.status === 'live' && (
                    used
                      ? <button className="ex-addbtn on" onClick={(e) => { e.stopPropagation(); onRemove(o.id) }} title="Remove from contract">✓</button>
                      : <button className="ex-addbtn" onClick={(e) => { e.stopPropagation(); onAdd(o.id) }} title="Add to contract">+</button>
                  )}
                </div>
              )
            })}
          </div>
        )
      })}
    </aside>
  )
}
