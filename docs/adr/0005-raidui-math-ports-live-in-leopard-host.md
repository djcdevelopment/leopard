# ADR-0005: The RaidUI math corpus ports into leopard-host artifact modules, not Tempo

Status: Accepted (2026-06-09)

## Context

RaidUI (the frozen JS beta at `D:\work\RaidUI`) carried an analysis layer that was never ported
when Tempo reconsolidated the parser: the six-signal timeline pack, the pull-diff engine, the
wipe-classification rule tree, the coverage quality model, group-structure affinity/clustering,
movement-phase segmentation, per-player scoring, and participant meters. Tempo's reconsolidation
ported the PARSER (lex → classify → segment → replay); the reducer/diagnostics layer above it
stayed JS-only — usable by nothing.

Live (between-pull insight) needed this math as evidence. Three placement options:

1. **Port into Tempo** (`Tempo.Projections`) — the "engine math belongs in the engine" instinct.
2. **Port into leopard-host** as artifact modules — the ShapeArtifact pattern.
3. **Keep calling the JS** (node sidecar / embedded runtime) — no port at all.

## Decision

Port into **leopard-host artifact modules** (option 2), one module per RaidUI suite, following
the established ShapeArtifact pattern: build at parse time from Tempo's replay/typed events,
cache per-night JSON next to the box score (`.signals.v1.json`, `.players.v1.json`,
`.affinity.v1.json`), serve via thin `/api/*` endpoints, feed Live's evidence block — with
**zero Tempo engine changes**.

Parity discipline: RaidUI's `__tests__` corpus is the oracle. Each port carries xUnit tests
derived from the JS tests' fixtures and invariants (suite grew 9 → 62 across the marathon), and
each was verified against a real log before the next port started. Deliberate deviations from
the JS are documented in each file's header (e.g. WipeClassifier's per-second signal adapter,
CoverageTimeline's zero-tank guard).

Scope discipline: the port audit also names what was **deliberately not ported**, with reasons —
prompt-assembly feature blocks (superseded by CanonicalContext/lens), compare-engine playback
orchestration (UI), per-frame signal variants (superseded by the artifact + classifier adapter),
the no-LLM phrase bank (Leopard's model is the renderer), guildops/celebration (parked product
areas). The corpus is fully accounted for: ported, superseded, or parked — nothing "pending."

## Consequences

- Leopard owns the analysis vocabulary it grounds insights in; Tempo stays a parser. The
  product can iterate on diagnostics without touching (or waiting on) the engine repo.
- The ShapeArtifact pattern is now load-bearing across nine modules — parse-time compute,
  per-night cache, thin API. A tenth module has an obvious shape to follow.
- The cost accepted: this math is C# in a product repo, not a reusable engine library. If a
  second Tempo consumer ever needs the same diagnostics, extraction into Tempo.Projections is
  a refactor, not a rewrite — the modules are already pure (inputs: replay frames/typed events;
  outputs: POCOs).
- RaidUI is now fully strip-mined: the JS beta can be archived without losing math.
- Decision driver: Live needed real evidence *that evening*, and the zero-Tempo-changes
  constraint (ADR-018 in Tempo frames Leopard as a consumer) made leopard-host the only
  placement that didn't serialize on a second repo's review cycle.
