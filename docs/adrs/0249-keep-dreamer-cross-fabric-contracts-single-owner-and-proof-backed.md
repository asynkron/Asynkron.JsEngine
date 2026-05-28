# ADR 0249: Keep Dreamer cross-fabric contracts single-owner and proof-backed

## Status

Accepted

## Context

Faktorial issue `autrun-diu3osh3kk68-13385ba687` and PR #2481 added the
`Cross-fabric contract surfaces` section to `docs/dreaming.md`. The delivery
was a bounded documentation slice: it added a Mermaid ownership and handoff map
plus explicit boundary contract rules for frontend, compilation, execution,
async, module/host, standard-library, and evidence surfaces.

Earlier Dreamer learn passes had already established that roadmap and
architecture language must separate proven behavior from directional targets.
The remaining drift risk was routing ambiguity. A future slice could still
mention several fabrics at once, imply broad delivery ownership, or cross from
compilation to execution, execution to async, or execution to host behavior
without naming the receiving contract and the proof needed to make that
handoff reviewable.

## Decision

Keep the Dreamer cross-fabric map single-owner and proof-backed.

- A Dreamer, roadmap, ADR, PR, or issue slice should name one primary owner
  surface before it claims a capability expansion.
- Crossing a fabric boundary is allowed only when the receiving contract is
  explicit. The handoff must say what the receiver consumes, owns, and proves.
- Frontend-to-compilation work must preserve typed AST invariants and make
  unsupported lowering shapes explicit.
- Compilation-to-execution work must keep execution on plan-owned artifacts.
  It must not introduce silent runner-time AST fallback widening.
- Execution-to-async work must preserve await/yield restart ownership and
  completion behavior at explicit suspension seams.
- Execution-to-module/host work may adapt host behavior, but it must not turn
  host-layer interoperability into a core evaluator parity claim.
- Execution-to-standard-library work must remain `JsValue`-native and preserve
  descriptor, brand, and observable built-in semantics.
- Every boundary claim needs focused semantic proof plus the canonical quality
  gate. Performance claims also need current profile or benchmark evidence.

## Consequences

- `docs/dreaming.md` is the central cross-fabric routing contract for Dreamer
  wording. Future changes should update that contract first instead of
  scattering equivalent ownership prose across unrelated architecture text.
- Review can reject broad capability language when a slice spans several
  fabrics without choosing a primary owner and naming proof for each handoff.
- The contract keeps big-to-small routing practical: broad architecture
  direction can exist, but implementation slices still land under one owned
  surface with observable proof.
- Documentation baseline signals, line counts, or Mermaid diagram presence
  remain traceability evidence only. They are not runtime, semantic, or
  performance proof.

## Evidence

- PR #2481 merged commit `16f37bb4a67dc735c0bea2e1da8d7d420a3bd2b6`.
- Build-stage commit `ff2b92c9ccb3396cc8e87b55404932a7671f12de` added only
  `docs/dreaming.md`.
- The merged diff added 25 lines and no runtime files.
- The issue build comment recorded `docs/dreaming.md` line count moving from
  281 to 306, matching the 25-line docs-only increase.
- Build-stage validation recorded `rtk git diff --check` passing and the
  working diff scope staying docs-only.

## Related

- Faktorial issue `autrun-diu3osh3kk68-13385ba687`
- PR #2481
- `docs/dreaming.md`
- `.claude/rules/roadmap-architecture-claims.md`
- ADR 0226:
  `docs/adrs/0226-keep-node-competitor-roadmap-milestones-evidence-gated.md`
