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
7. Before any roadmap, dream, ADR, PR, or issue text claims a capability
   expansion, run the claim-discipline checklist explicitly: name the owner
   surface down to module plus concrete file/class boundary, prove semantics on
   the owning focused pack before widening, attach profile or benchmark evidence
   when the claim is performance-related, and keep boundary wording explicit
   about what remains host-layer, prototype-only, or otherwise directional.
8. When a Dreamer or roadmap refresh mentions unified-bytecode, CommonJS/Node
   compatibility, or async-generator seam closure, restate the current accepted
   boundary instead of using broad capability labels. Documentation run signals
   such as line counts, timestamps, or diff stats are traceability only; they
   are not runtime proof.
9. When `docs/dreaming.md` adds or revises a proven-now vs directional-next
   contract or ownership-routing section, treat that section as the durable
   boundary map for future Dreamer wording. Update the central contract first
   when capability status changes, and do not scatter equivalent boundary prose
   into unrelated architecture text.

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

Faktorial issue `autrun-ditlbq5q7p9k-29b6eca348` / PR #2430 added the
`docs/dreaming.md` claim discipline checklist after the Dreamer document still
needed a harder gate between useful architecture aspiration and capability
claims. WHY: without the checklist, future agents can repeat the same failure
mode by naming a capability expansion in roadmap or PR prose while omitting the
owner surface, focused semantic proof, performance evidence, or host/prototype
boundary that keeps the claim reviewable.

Faktorial issue `autrun-diu14wtcpia8-e0ec95bea3` / PR #2448 showed that the
checklist still needs concrete boundary restatement in recurring Dreamer runs.
WHY: phrases such as "unified-bytecode property access", "CommonJS
compatibility", or "async-generator seam closure" can read as broad runtime
parity unless the document says what is currently accepted, what remains
host-layer or prototype-only, and what still needs focused proof. That run
also recorded documentation baseline/final signals; future agents should keep
those as audit signals rather than treating them as semantic or performance
evidence.

Faktorial issue `autrun-diu2eun13bqo-5bf42db896` / PR #2472 tightened
`docs/dreaming.md` into a routing guide by adding an explicit proven-now vs
directional-next contract and ownership routing by concern. WHY: prior Dreamer
iterations had accumulated useful caveats, but future agents still had to infer
capability status and owner surfaces from broader architecture prose. Keeping
the status table and routing section as the central contract prevents the same
drift from reappearing as scattered Node.js/CommonJS, async, or unified-bytecode
claims.
