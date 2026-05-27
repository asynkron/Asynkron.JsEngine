# Unified Bytecode Prototypes

When extending the unified bytecode prototype, keep it IR-owned, internal, and
all-or-nothing until a separate routing issue proves production readiness.

## Rules

1. Use `ExecutionPlan` as the prototype compiler input. Do not create a
   parallel AST-to-unified-bytecode compiler for shapes that the current IR
   already lowers and annotates.
2. Keep eligibility at compile time, including function kind. Unsupported
   statement shapes, expression ops, identifiers, async/generator functions,
   local/declaration forms outside the exact accepted slice, control flow
   outside the accepted branch-plus-canonical-loop-back-edge slice, or dynamic
   shapes must return an unsupported reason before VM execution.
   Do not infer sync-only eligibility from `ExecutionPlan` shape alone.
3. Do not add fallback inside `UnifiedBytecodeVirtualMachine` to
   `ExpressionProgram` evaluation, AST evaluation, or `ExecutionPlanRunner`.
   VM execution should only execute bytecode the unified compiler emitted.
4. Do not route normal production execution through the unified VM in a
   prototype-expansion issue. Runtime routing needs its own issue and proof
   pack.
5. When expanding linear declaration/return packs, flatten only the supported
   `ExpressionProgram` operations into unified instructions. Identifier reads
   should become `LoadSlot`, literals should become program-owned
   `LoadLiteral` entries, and binary operators should stay limited to the
   explicitly proven numeric VM surface. Do not introduce an
   `EvalExpressionProgram` opcode or a runtime callback into the existing
   expression interpreter.
6. For each accepted shape, add focused tests for the emitted unified opcode
   stream, a minimal execution result, and at least one nearby unsupported
   shape that declines cleanly. When an accepted body shape can also appear in
   async or generator functions, include function-kind negative tests.
7. Keep JavaScript semantic claims narrow. A prototype op such as numeric
   `Add` proves only the tested VM behavior; full JavaScript operator coercion
   requires an explicit migration and parity proof.
8. When expanding across branch/control flow, keep accepted CFG ownership
   compiler-side and explicit. Branch shapes plus one canonical
   condition-first loop back-edge IR shape are accepted; all other
   loop/control-flow families must be rejected before VM execution. Compile with
   an IR-instruction-index to unified-bytecode-PC map, patch forward branch/jump
   operands after targets are emitted, and reject unsupported branch payloads or
   non-canonical loop shapes before execution. Do not treat this bounded loop
   support as broad loop support or as permission to call back into existing
   evaluators.
9. When adding or using production-routing eligibility, keep it decline-first
   and narrower than the prototype compiler until runtime proof widens it. Use
   `ExecutionPlan` plus explicit activation metadata, return stable decline
   codes/reasons before VM execution, and accept only the exact production
   opcode subset that has been proven. The current production subset includes
   branch joins, direct branches, joined-local updates, and the canonical
   condition-first loop back-edge only through the existing compiler-owned
   shapes; do not add source-syntax exceptions or a selector-side second CFG
   recognizer. `Binary` is production-eligible only for the explicitly proven
   operator subset (`+`, `-`, `*`, `/`, `%`, `==`, `<`, `<=`, `>`, `>=`) and
   must execute through the existing `JsValue` operator helpers with an
   `EvaluationContext`, not direct numeric extraction. Any new production
   Binary operator must update the selector, unified compiler allowlist, and VM
   semantics in the same slice, with positive selector/route proof and a nearby
   unsupported operator decline/no-route proof. Unsupported Binary operators
   must still decline as `PrototypeOnlyBinaryOpcode` with operator-specific
   diagnostics, and labels, break/continue, calls, dynamic lookup,
   noncanonical loops, and unsupported payloads must decline before VM
   execution.
10. When invoking production unified bytecode from sync calls, keep the bridge
    slot-layout owned and fast-path ordered. Direct specialized simple-return
    binary/chain shortcuts stay ahead of unified bytecode. The production
    unified route intentionally runs ahead of the broader `SyncIrCallTrampoline`
    so accepted branch, join, and canonical-loop shapes are not swallowed by
    the trampoline, then the generic simple IR activation runner remains behind
    both. Populate an invocation-local slot span from `ActivationSlotShape` by
    filling `undefined` and writing parameters through `ParameterSlotIndices`;
    do not create a `JsEnvironment`, call `ExecutionPlanRunner`, or add VM
    fallback for accepted programs. Prove selected routing, faster-route
    preservation, and nearby declines through public invocation tests plus the
    activation proof pack. If a future slice changes priority again, make that
    explicit and prove the older route remains covered.
11. When updating docs, ADRs, roadmap text, or evidence reports for unified
    bytecode production routing, treat ADR 0210 as the current production
    boundary and keep ADR 0204/#2227 direct-branch wording historical unless a
    newer accepted ADR supersedes it. The docs must state the no-mixed-execution
    rule, list the exact eligible opcode/control-flow/operator families, keep
    unsupported shapes as pre-VM declines, and describe Batch 5 memory/profile
    evidence as allocation stability only unless a separate before/after proof
    justifies a performance-improvement claim.
12. When defining property-read production eligibility, keep candidate
    recognition separate from VM acceptance until the same slice adds compiler
    opcodes, VM semantics, route-priority proof, and negative no-route tests.
    Direct named candidates are only activation-resolved base reads followed by
    non-optional `GetNamedProperty`. Direct computed candidates must preserve
    the exact ordinary-read lowering sequence:
    `RequireObjectCoercible(Depth: 1)`, then `ResolvePropertyKey`, then
    non-optional `GetComputedProperty`, with only production-safe base/key
    loads before it. Recognized candidates that lack VM support must decline as
    `PropertyReadCandidateRequiresVmSupport`, not compile or run. Adjacent
    families such as calls/constructs, member call targets, writes, updates,
    delete, `super`, `this`, optional chains, object literal/spread, dynamic
    lookup, and out-of-boundary computed reads need stable pre-VM decline codes
    plus concrete source-example tests. Also scan all expression-program-bearing
    instructions, including evaluate-and-discard and throw payloads, before
    generic compiler fallback so property-read hazards cannot hide outside
    return expressions. Computed-read negative coverage must include unsupported
    key payloads, not only unsupported bases or final `GetComputedProperty`
    shapes. Keep concrete examples such as `box[{ value: 1 }]` and
    `box[{ ...source }]` declining as `ObjectLiteralOrSpreadDependency` so
    key-payload hazards stay visible before generic property-read boundary
    declines.
13. When making property-read candidates executable in production unified
    bytecode, keep the read semantics VM-owned and fallback-free. Named keys
    belong in `UnifiedBytecodeProgram.StringConstants` and must execute through
    an owned `GetNamedProperty` opcode. Computed reads must emit the exact
    ordinary-read sequence `RequireObjectCoercible(Depth: 1)`,
    `ResolvePropertyKey`, then `GetComputedProperty`, and the VM must use the
    existing `JsOps` property-key and property-lookup helpers with the active
    `EvaluationContext`. Do not satisfy property-read execution by calling back
    into `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation. Future
    optional-chain, member-call, write/update, `super`, dynamic lookup, or
    richer computed-key support needs its own selector/compiler/VM/proof slice
    instead of widening this first executable boundary.

## Why

Issue #2118 / PR #2137 introduced the first unified bytecode slice for
`function add(x, y) { return x + y; }`. The useful decision was not just the
new files; it was the boundary: compile from existing `ExecutionPlan`, flatten
only the proven return-expression payload, keep the VM fallback-free, and leave
production routing untouched. Future agents should preserve that boundary so
unified-bytecode coverage gaps stay explicit instead of being masked by the
existing statement IR, expression bytecode, or AST evaluators.

Issue #2139 / PR #2144 expanded the prototype to an exact linear local
declaration plus return shape and then fixed review feedback by passing
function-kind metadata into `UnifiedBytecodeCompiler.TryCompile`. The lesson is
that async and generator bodies can look shape-compatible while requiring
promise, iterator, and suspension semantics that the current unified VM does
not implement. Function kind must stay part of the compile-time eligibility
contract.

Issue #2158 / PR #2162 expanded the same prototype into a small linear sync
expression pack with multiple declarations, literals, and numeric binary
operators. The lesson is that this expansion must still own the bytecode it
executes: literals belong in `UnifiedBytecodeProgram`, supported expression ops
are flattened into unified instructions, and unsupported statements or
expression ops decline before execution. Adding a generic expression-program
eval opcode would hide coverage gaps and make the fallback-free VM boundary
untrue.

Issue #2166 / PR #2173 crossed the prototype from linear body walking into
acyclic branch CFG compilation. The lesson is that branch support needs an
explicit bytecode-PC owner: map IR instruction indices to emitted PCs and patch
`JumpIfFalse` and `Jump` targets after blocks are emitted.

Issue #2182 / PR #2186 then extended that boundary to one canonical
condition-first back-edge IR shape (currently produced by guarded `while` and
equivalent condition-only `for` forms without initializer/post-update or loop
control statements). The lesson is to keep this loop support narrow and
compiler-owned: accept only the proven canonical IR topology, reject other
loop/control-flow families before unsupported details hide the real boundary,
and keep production routing unchanged. The review correction on #2182 is part
of the rule: this is not a source-syntax `while` exception; source forms are
eligible only when lowering produces the same condition-first back-edge shape.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-de-8d0f3a4e16`
and PR #2205 introduced the first production-routing eligibility selector. The
lesson is that production eligibility is a separate contract from prototype
compile coverage: the first route accepts only neutral slot/literal/store/return
bytecode and declines async/generator functions, captured or dynamic activation,
arguments-object dependency, `this`, `new.target`, calls, dynamic lookup,
labels, break/continue, and prototype-only opcodes before VM execution.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-aa82d6b615`
and PR #2217 made that selector execute in production sync invocation for the
first time. The lesson is that runtime routing is a three-way contract between
existing sync fast-path ordering, the decline-first unified-bytecode selector,
and the `ActivationSlotShape` slot bridge. Future agents should not bypass that
bridge with source-shape checks, environment creation, runner callbacks, or VM
fallbacks.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-c0e3afa6d7`
and PR #2231 hardened the Binary production boundary before branch or loop
routing can use it. The lesson is that prototype Binary support and numeric VM
parity do not make `Binary` production-safe. Production eligibility must decline
the operator family first, include operator-specific diagnostics, and keep
branch/loop structural routing from admitting unproven condition semantics.

Issue #2227 / PR #2239 admitted the first production `JumpIfFalse` shape:
a single direct forward branch-return program with immediate return arms and no
`Jump`. The review correction is part of the rule. The invocation proof first
conflicted with restored `SyncIrCallTrampoline` priority, then was fixed by
using a local selector shape that still lowers to the accepted branch-return
program while avoiding the existing trampoline shortcut. Future agents should
prove production routing with a route-discriminating shape, not by moving
unified bytecode ahead of older fast paths. WHY: the incident showed that
selector acceptance and invocation routing are separate contracts; a source
shape can be eligible for unified bytecode but still correctly execute through
a higher-priority fast path.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-4fb4d210a6`
and PR #2243 widened production routing to branch joins, joined-local updates,
comparison conditions, string/coercing Binary use, and the canonical
condition-first loop shape. The lesson is that this was safe only after the VM
stopped using direct numeric Binary operations and reused the existing
`JsValue` operator helpers with the active `EvaluationContext`. The delivery
also intentionally moved unified production routing ahead of the broad
`SyncIrCallTrampoline` while keeping direct specialized binary shortcuts ahead
of unified bytecode. WHY: the incident showed that admitting control-flow
opcodes is not the decision by itself; operator semantics, compiler-owned CFG
shape, route priority, and public route-log evidence must move together.

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-2dd33add2a`
and PR #2254 updated the ADR, roadmap, and performance evidence after Batch 5.
The lesson is that stale docs can become a routing hazard: ADR 0204/#2227
direct-branch text remained useful history, but ADR 0210 owned the current
branch-join/canonical-loop production boundary. WHY: the issue existed because
future agents needed the exact eligible set, unsupported declines,
no-mixed-execution rule, and allocation-stability-only proof language in the
same maintained surfaces before widening production eligibility again.

Issue #2256 / PR #2261 widened the ADR 0210 production Binary subset by adding
only loose equality (`BinaryOperator.Equal`, `==`). The lesson is that even a
single operator widening needs paired selector, unified compiler allowlist, VM
semantics, public route-log proof, and no-route proof for a nearby unsupported
operator. The VM used `JsOps.LooseEquals(left, right, context)` instead of a
direct host comparison, while strict equality (`===`) stayed declined and
route-negative. WHY: the issue was a roadmap follow-up specifically to prevent
selector-only widening or mixed execution after ADR 0210; accepting `==`
without compiler/VM parity and branch-shaped route evidence would have made the
production boundary look wider than the runtime semantics actually proved.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-define-and-8d40cdb281`
and PR #2288 defined the first production property-read boundary as a
selector-only contract. The lesson is that property reads are observable even
when the lowered operation names look simple: ordinary computed reads must keep
`RequireObjectCoercible(Depth: 1) -> ResolvePropertyKey -> GetComputedProperty`
in order, optional chains and adjacent write/call/delete/super/object-literal
families must stay declined, and recognized candidates still decline until the
unified compiler and VM execute them directly. WHY: the issue existed to keep
future property-read widening from admitting source-shaped or opcode-shaped
candidates into a fallback-free production VM before the executable semantics
and route proof exist.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-2-uni-990bcd3283`
and PR #2311 made that boundary executable by adding owned property-read
opcodes, `StringConstants` operand storage, `JsOps`-backed lookup/key
semantics, production eligibility acceptance, and public invocation proof.
WHY: the incident closed the deliberate ADR 0218 gap where property-read
candidates were recognized but declined until the VM could execute them
directly. Future widening must preserve the same all-at-once contract so
observable property semantics do not slip through a mixed
`ExpressionProgram`/runner/AST fallback.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-3-com-ca10aa7559`
and PR #2321 completed the property-read boundary proof by adding explicit
declines for unsupported computed property-key payloads: `box[{ value: 1 }]`
and `box[{ ...source }]`. WHY: object literal/spread operations can appear in
the key-evaluation payload before the final computed property read. Without
source examples for those payloads, future widening can misclassify them as a
generic property-read boundary miss or lose the guardrail while merging adjacent
property-read batches.

Related ADRs:
- `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- `docs/adrs/0186-keep-unified-bytecode-function-kind-eligibility-explicit.md`
- `docs/adrs/0189-keep-unified-bytecode-linear-expression-packs-flattened.md`
- `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`
- `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- `docs/adrs/0205-keep-unified-bytecode-binary-production-eligibility-operator-explicit.md`
- `docs/adrs/0208-keep-unified-bytecode-branch-production-routing-shape-discriminated.md`
- `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0222-keep-unified-bytecode-two-hop-named-property-read-boundary-owned.md`
