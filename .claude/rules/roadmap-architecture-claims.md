# Roadmap Architecture Claims

When editing `docs/roadmap.md`, `docs/dreaming.md`, or architecture docs for
Node.js-competitor direction, keep milestone language evidence-gated and
boundary-explicit.

## Rules

1. Separate currently proven behavior from aspirational targets. Do not turn
   Node.js-competitor direction into a claim of current Node.js, CommonJS,
   module, async, or broad runtime parity.
2. Name owner surfaces for each milestone or roadmap claim: runtime files,
   host-layer files, focused tests, profile entries, ADRs, or maintained docs.
   Broad labels such as "module support" are not enough for delivery ownership.
3. Keep engine/runtime behavior distinct from host interop or demo-layer
   behavior. Node-style host execution, CommonJS shims, or demo projects can be
   evidence for a host-layer milestone, but they are not core evaluator parity
   evidence by themselves.
4. Treat evidence gates as required proof, not as completed proof. Only say a
   test pack, profile row, or docs anchor is already satisfied when the current
   issue context or current worktree evidence proves it.
5. For architecture-alignment milestone sections, prefer the #2342 shape:
   owner surfaces, currently proven behavior, aspirational targets, and
   evidence gates.
6. When refreshing `docs/dreaming.md`, keep it as a greenfield architecture
   target rather than a runtime proof surface. Product language, Mermaid
   diagrams, and "fabric" component names must stay paired with explicit
   current-reality constraints and non-goals for module/runtime parity, host
   interop, async seam closure, and bounded bytecode routing.

## Why

Issue #2342 / PR #2351 added the first concrete roadmap milestone section that
connects the Node.js-competitor dream to near-term module/runtime, host interop,
and async seam delivery. The durable lesson is that the architecture narrative
is useful only while it remains explicit about current proof versus future
targets. Without this rule, future roadmap refreshes can accidentally convert a
north-star statement, Node-style demo, or host/CommonJS compatibility surface
into a broad parity claim unsupported by focused tests, profile evidence, or
runtime ownership.

Faktorial issue `autrun-ditfsckhviao-c5fdd52294` / PR #2363 repeated the same
risk on `docs/dreaming.md`: the document needed a stronger greenfield target,
but only after anchoring it to #2342 current-reality constraints for
module/runtime parity, host interop, async-generator seam risk, and bounded
bytecode routing. Future Dreamer or architecture-doc passes should preserve
that pairing instead of letting diagrams or product-language cleanup imply
runtime parity.

Related ADR:
`docs/adrs/0226-keep-node-competitor-roadmap-milestones-evidence-gated.md`.
