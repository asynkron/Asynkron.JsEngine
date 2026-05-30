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
10. When Dreamer, roadmap, ADR, PR, or issue text crosses the
    `docs/dreaming.md` cross-fabric contract map, name one primary owner
    surface and the receiving contract before claiming delivery. Do not present
    multi-fabric wording as complete unless every handoff has focused semantic
    proof, canonical quality-gate evidence, and profile or benchmark evidence
    when the claim is performance-related.
11. When `docs/dreaming.md` decomposes architecture into delivery packets, keep
    each packet tied to one primary owner module, an explicit cross-fabric
    boundary contract, and the exact proven-now surface plus directional-next
    boundary before implementation starts. Keep Node/CommonJS, async-generator
    seam, and unified-bytecode production-routing wording directional unless
    the packet itself carries the focused proof and profile or benchmark
    evidence needed for the stronger claim.
12. When `docs/dreaming.md` or roadmap prose scopes #2342 Milestone C, keep
    Async and Concurrency Fabric as the primary owner for resume scheduling and
    async-generator continuation state. Treat Execution Fabric as the source of
    consumed await/yield suspension and completion-lane contracts, and Host as a
    wakeup/callback receiving boundary. Do not call the async seam closed until
    focused async-generator proof, canonical quality evidence, and required
    profile evidence are attached.
13. When a recurring Dreamer slice adds a new `this run` section or signal
    block in `docs/dreaming.md`, reclassify older run-scoped headings and
    sentences as `previous run` or historical evidence. Leave only the active
    delivery's section and signal labels as `this run`; stale current-run
    wording makes owner and proof status ambiguous.
14. When `docs/dreaming.md` contains both a routing tier model and a
    speculative/JIT tier model in the same document, label each section's
    tier-zero explicitly to prevent cross-section confusion. The routing
    4-tier model numbers hot-first (Tier 0 = UnifiedBytecodeVM, the proven
    production route); the JIT speculative tier model uses the opposite
    convention (Tier 0 = interpreter fallback, Tier 1 = optimized VM). Both
    numbering schemes can coexist, but each diagram and invariant block must
    carry a scoped prefix or parenthetical (e.g., "routing Tier 0" vs
    "speculative Tier 0") so readers and future Dreamer agents do not treat
    the two as the same numbering. Do not use bare "Tier N" labels in a
    section without establishing which convention applies.
15. When a roadmap, dream, ADR, or PR text cites a PR number or commit SHA as
    evidence for a landed capability or perf slice, verify each citation against
    real git history before writing it. Re-derive the owner of every reference
    with `git log --grep="(#NNNN)"` (or `git show <sha> --stat`) and confirm the
    referenced change actually did what the bullet claims. Do not cite a PR
    number that does not resolve to a commit, do not cite an empty agent commit
    (one whose diff is empty versus its parent) as a feature delivery, and do
    not reattribute a real PR's work to an unrelated claim. A plausible-looking
    `#NNNN` near the current head is not evidence; the resolved commit and its
    diff are.

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

Faktorial issue `autrun-diu3osh3kk68-13385ba687` / PR #2481 added the
`docs/dreaming.md` cross-fabric contract surfaces map. WHY: the earlier routing
guide named owner concerns, but it did not force a single primary owner plus an
explicit receiving contract for boundary work. Future Dreamer and roadmap
passes should use that map to keep big-to-small routing reviewable instead of
letting frontend, compilation, execution, async, host, standard-library, and
evidence ownership blur into one broad capability claim.

Faktorial issue `autrun-diu4yqb9zgww-22589f49e2` / PR #2493 added the
`docs/dreaming.md` delivery packet decomposition after the cross-fabric map
still needed a one-owner implementation path. WHY: without packet-level rules,
future Dreamer slices can name the right fabrics but still blur review
ownership by combining owner module, boundary contract, proof packet, and
roadmap/ADR traceability into one broad delivery claim. Keeping each packet
tied to one primary owner, exact proven-now surface, and directional-next
boundary preserves #2342 claim discipline while async-generator and
unified-bytecode work remains deliberately bounded.

Faktorial issue `autrun-diu68o5458hc-6ec572012c` / PR #2504 narrowed #2342
Milestone C routing in `docs/dreaming.md` to the Async and Concurrency Fabric.
WHY: Milestone C wording often spans execution opcodes, scheduler resume state,
host callbacks, and evidence gates in one sentence. Without this rule, future
Dreamer or roadmap slices can accidentally turn an Execution -> Async -> Host
handoff contract into a broad async-seam-closure claim before focused
async-generator proof and canonical quality evidence exist.

Faktorial issue `autrun-diu8sjtzmb34-e2a933a687` / PR #2528 closed a
review-requested Dreamer ambiguity after a new "Capability lifecycle control
plane (this run)" section left the older Milestone C slice also labeled as
"this run". WHY: multiple current-run labels made it unclear which architecture
slice represented the active delivery and which was carried historical
evidence. Future Dreamer slices should demote older run-scoped headings and
sentences while preserving the current delivery's traceability signals.

Faktorial issue `autrun-divg7tnacvm8-b71e7281d4` / PR #2647 introduced both a
routing 4-tier model (Tier 0 = UnifiedBytecodeVM, hot path) and a speculative
JIT tier section (Tier 0 = interpreter fallback) in the same document. The two
sections used inverted Tier-0 conventions without disambiguation labels, making
it ambiguous which "Tier 0" a future agent or reader should apply when referencing
tier numbering. WHY: future Dreamer runs or architecture slices that read either
section in isolation will carry the wrong tier-mapping into new prose unless the
section-scoped convention is explicit.

Faktorial issue `autrun-divruulx5lr4-9e45957c7e` / PR #2669 (recurring
"Roadmapper" child) shipped a first build commit whose Current State bullets
cited PR numbers that do not exist in git history (`#2654`, `#2656`, `#2652`),
cited an empty agent commit (`#2658`) as a feature delivery, and reattributed
several real PRs to the wrong work (`#2646`, `#2644`, `#2650`, `#2651`,
`#2659`, `#2660`). The "Roadmap fidelity" quality gate caught it, and the fix
re-derived every citation from `git log --grep="(#NNNN)"` against the worktree's
actual history. WHY: an automated roadmap refresh that pattern-matches recent
`#NNNN`-looking numbers near HEAD will fabricate or misattribute provenance,
which then reads as proof. Rule 15 forces each PR/commit citation to be resolved
to a real commit and its diff before it can be written as evidence — the same
evidence-gating discipline the rest of this file applies to capability claims,
extended to provenance citations.

Related ADRs:
- `docs/adrs/0226-keep-node-competitor-roadmap-milestones-evidence-gated.md`
- `docs/adrs/0249-keep-dreamer-cross-fabric-contracts-single-owner-and-proof-backed.md`
- `docs/adrs/0257-keep-dreamer-milestone-c-async-owner-contract-explicit.md`
