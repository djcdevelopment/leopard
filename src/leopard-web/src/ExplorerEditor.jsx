import React from 'react'

// The center editor pane: a "compiled.context" document tab over the contract XML, with
// hand-rolled syntax highlighting (no library) and the HomeSite-style status line.
// IMPORTANT: this pane is display-only. The bytes sent to the model are always the full
// xml prop; the "folded" view collapses slice payloads to self-closing tags purely for
// reading — a display transform, never a different serialization.

// Tokenize one line of XML into highlighted spans.
function highlightLine(line, key) {
  const out = []
  const re = /(<\/?[\w.@-]+|\/?>|[\w.@-]+=)|("(?:[^"]*)")/g
  let last = 0
  let m
  let i = 0
  while ((m = re.exec(line)) !== null) {
    if (m.index > last) out.push(line.slice(last, m.index))
    if (m[1] !== undefined) {
      out.push(<span className={m[1].endsWith('=') ? 'x-attr' : 'x-tag'} key={`${key}-${i++}`}>{m[1]}</span>)
    } else {
      out.push(<span className="x-str" key={`${key}-${i++}`}>{m[2]}</span>)
    }
    last = m.index + m[0].length
  }
  if (last < line.length) out.push(line.slice(last))
  return out
}

// Display-only folding: collapse each <slice …>payload</slice> block to <slice … />.
function foldXml(xml) {
  return xml.replace(/<slice ([^>]*)>[\s\S]*?<\/slice>/g, '<slice $1 />')
}

export default function ExplorerEditor({ xml, sliceCount, totalTok, coverage, folded, onToggleFold }) {
  const display = folded ? foldXml(xml) : xml
  const lines = display.split('\n')
  return (
    <section className="ex-editor">
      <div className="ex-edtabs">
        <span className="ex-edtab active"><span className="ex-edtab-dot" /> compiled.context</span>
        <button className="ex-fold" onClick={onToggleFold} title={folded ? 'Show slice payloads' : 'Collapse slice payloads (display only — full payload is always sent)'}>
          {folded ? '+ payloads' : '− payloads'}
        </button>
      </div>
      <pre className="ex-xml mono">
        {lines.map((line, n) => (
          <div className="ex-xml-line" key={n}>
            <span className="ex-ln">{n + 1}</span>
            <span className="ex-code">{highlightLine(line, n)}</span>
          </div>
        ))}
      </pre>
      <div className="ex-status">
        <span><span className="ex-dot live" /> {sliceCount} slice{sliceCount === 1 ? '' : 's'} · ~{totalTok >= 1000 ? `${(totalTok / 1000).toFixed(1)}k` : totalTok} tokens · {coverage}</span>
        <span className="muted">{lines.length} lines{folded ? ' · payloads folded (full bytes sent)' : ''}</span>
      </div>
    </section>
  )
}
