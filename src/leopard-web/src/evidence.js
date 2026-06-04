// First light ships one real raid as the evidence substrate: The Voidspire (Heroic,
// 2026-04-18), generated one-shot by `Tempo.Diagnostics --export-evidence` (no running
// engine — see dependencies/tempo-engine/seam.md). The file lives in public/ and is
// served as a static asset. Later slices let the user point at their own log.

export async function loadEvidence() {
  const res = await fetch('/voidspire-evidence.md')
  if (!res.ok) throw new Error(`evidence load failed: HTTP ${res.status}`)
  return await res.text()
}

export const SAMPLE_LABEL = 'The Voidspire · Heroic · 2026-04-18'

// The seeded questions embody the vision: momentum over perfection, recognize the
// unnoticed, start a conversation. They also teach the user what to ask.
export const SEEDED_QUESTIONS = [
  'What should we celebrate from this night?',
  'What changed across our Crown of the Cosmos attempts?',
  'Which pull felt different, and why?',
  'What stabilized, and what is still costing us?',
]
