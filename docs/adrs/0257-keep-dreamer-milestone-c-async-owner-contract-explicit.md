# ADR 0257: Keep Dreamer Milestone C async-owner contracts explicit

## Status

Accepted

## Context

Faktorial issue `autrun-diu68o5458hc-6ec572012c` and PR #2504 refined
`docs/dreaming.md` for the Dreamer recurring architecture slice. The delivery
added a bounded Milestone C architecture section that names Async and
Concurrency Fabric as the primary owner while treating Execution Fabric and
Module/Host Fabric as explicit handoff contracts.

The recurring ambiguity was not whether async seam closure matters. The risk
was that Milestone C wording could combine execution opcodes, scheduler resume
state, host callbacks, and evidence gates into one broad "seam closure" claim.
That would weaken ADR 0249's single-owner cross-fabric rule and make future
implementation slices hard to review.

## Decision

Keep Dreamer Milestone C async-seam wording owner-explicit and proof-gated.

- Milestone C slices that span execution, scheduler, and host wakeup concerns
  must name Async and Concurrency Fabric as the primary owner unless current
  evidence proves another owner.
- Execution Fabric provides the consumed contract: await/yield suspension
  opcodes, completion lanes, restart ordering, and abrupt-completion semantics.
  The async owner must preserve that contract and must not widen silent
  runner-time AST fallback.
- Host wakeup is a receiving boundary: callback enqueue and wake signal
  delivery can be adapted at the host boundary, but that does not transfer core
  evaluator ownership to host-layer code.
- Seam-closure text remains directional until a focused async-generator proof
  pack, canonical quality-gate evidence, and any required profile evidence are
  attached to the delivery slice.
- Documentation line counts, Mermaid diagrams, and run timestamps are
  traceability signals only. They are not semantic proof that the async seam is
  closed.

## Consequences

- Future Dreamer and roadmap text can describe the greenfield Milestone C
  target without implying that async-generator seam closure is already proven.
- Review can reject Milestone C wording that bundles execution, scheduler, host
  wakeup, and proof ownership into a single broad capability claim.
- Implementation packets can still cross from Execution to Async or Host, but
  the receiving contract must be named before the packet claims delivery.
- ADR 0249 remains the general Dreamer cross-fabric contract; this ADR records
  the Milestone C-specific async ownership rule.

## Evidence

- PR #2504 merged commit
  `772a1f0bc5dc87929a53117fa02d6ca1f839c8c9`.
- Build-stage commit `3de2ac29` changed only `docs/dreaming.md` with 29
  insertions and 5 deletions.
- The merged diff added a "Bounded architecture improvement slice" section
  with an Execution -> Async -> Host/Evidence Mermaid contract and Milestone C
  routing rules.
- The build-stage issue comment recorded `rtk git diff --check` passing.
- The same comment recorded `docs/dreaming.md` moving from 329 to 353 lines for
  this run, a +24 line docs-only signal.

## Related

- Faktorial issue `autrun-diu68o5458hc-6ec572012c`
- PR #2504
- `docs/dreaming.md`
- `.claude/rules/roadmap-architecture-claims.md`
- ADR 0226:
  `docs/adrs/0226-keep-node-competitor-roadmap-milestones-evidence-gated.md`
- ADR 0249:
  `docs/adrs/0249-keep-dreamer-cross-fabric-contracts-single-owner-and-proof-backed.md`
