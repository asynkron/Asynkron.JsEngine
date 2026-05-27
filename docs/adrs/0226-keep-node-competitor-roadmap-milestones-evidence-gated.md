# ADR 0226: Keep Node.js-competitor roadmap milestones evidence-gated

## Status

Accepted

## Context

Issue #2342 and PR #2351 translated `docs/dreaming.md` from an architecture
north star into concrete near-term roadmap milestones for module/runtime
compatibility, host interop, and async runtime seams.

The useful decision was not a runtime implementation change. It was a roadmap
governance boundary: Node.js-competitor language is valuable direction, but it
can easily overclaim current parity if roadmap text does not separate proven
engine behavior from aspirational compatibility targets. The same wording can
also blur core evaluator/runtime behavior with host-layer demonstrations such
as Node-style examples or CommonJS compatibility surfaces.

## Decision

Keep Node.js-competitor roadmap and architecture milestones evidence-gated.

- Milestones must name owner surfaces before they imply delivery ownership.
  Use concrete runtime, host, test, profile, or docs anchors rather than broad
  subsystem labels.
- Milestones must separate currently proven behavior from aspirational targets.
  Do not turn a directional milestone into a Node.js, CommonJS, module, async,
  or broad runtime parity claim.
- Host interop and demo-layer behavior must stay visibly distinct from core
  evaluator/runtime behavior. Node-style host execution can support a roadmap
  target, but it is not core engine parity evidence by itself.
- Evidence gates must be explicit and not self-satisfying. A roadmap section can
  require focused tests, profile rows, or docs anchors without claiming those
  gates have already been satisfied.
- Future milestone updates should preserve the #2342 shape: owner surfaces,
  currently proven behavior, aspirational targets, and evidence gates.

## Consequences

- Roadmap work can advance the Node.js-competitor narrative without weakening
  the repository's evidence-first delivery policy.
- Future agents have a durable structure for converting architecture direction
  into delivery slices while keeping parity language bounded.
- Host interop, module/runtime, and async seam work can progress independently
  under their own focused proof packs instead of being bundled into a vague
  compatibility claim.

## Evidence

- Issue #2342 required concrete milestones for module loading/runtime
  compatibility, host interop, and async runtime seams, with evidence gates and
  proven-vs-aspirational wording.
- PR #2351 / commit `a501ce93` added
  `Node.js-Competitor Architecture Alignment Milestones (#2342)` to
  `docs/roadmap.md`.
- The merged PR commit `aa2b5d96` preserved the delivery as a documentation-only
  roadmap slice.
- The delivery comment recorded one changed artifact, `docs/roadmap.md`, with
  49 insertions and 2 deletions, and no runtime behavior changes.

## Related

- Issue #2342
- PR #2351
- `docs/dreaming.md`
- `docs/roadmap.md`
- `.claude/rules/roadmap-architecture-claims.md`
