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
   branch joins, direct branches, joined-local updates, canonical
   condition-first loop backedges, simple do-while consequent backedges, and
   proven unlabeled loop-control target jumps including direct break/continue
   and for-style update continue targets, all through the existing
   compiler-owned shapes; do not add source-syntax exceptions or a
   selector-side second CFG recognizer. `Binary` is production-eligible only
   for the explicitly proven operator subset (`+`, `-`, `*`, `/`, `%`, `==`,
   `<`, `<=`, `>`, `>=`) and
   must execute through the existing `JsValue` operator helpers with an
   `EvaluationContext`, not direct numeric extraction. Any new production
   Binary operator must update the selector, unified compiler allowlist, and VM
   semantics in the same slice, with positive selector/route proof and a nearby
   unsupported operator decline/no-route proof. Unsupported Binary operators
   must still decline as `PrototypeOnlyBinaryOpcode` with operator-specific
   diagnostics, and labels, unproven or labeled loop-control shapes, calls,
   dynamic lookup, noncanonical loops, and unsupported payloads must decline
   before VM execution.
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
    activation proof pack. Also keep the ordinary-sync route order itself
    source-gated inside `TryInvokeIrFast<TArgs>(...)`: specialized binary
    routes first, accepted production unified bytecode before
    `SyncIrCallTrampoline`, and generic `ExecutionPlanRunner` last. WHY: issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-8f4d6fd0f4`
    / PR #2623 found the production code already had the intended order, but
    route logs alone did not guard the whole fallback chain from drifting back
    toward older ADR 0204/0208 wording. If a future slice changes priority
    again, make that explicit and prove the older route remains covered.
11. When updating docs, ADRs, roadmap text, or evidence reports for unified
    bytecode production routing, treat ADR 0253 as the current loop-control
    production widening layered on ADR 0210, and keep ADR 0204/#2227
    direct-branch wording historical unless a newer accepted ADR supersedes it.
    The docs must state the no-mixed-execution rule, list the exact eligible
    opcode/control-flow/operator families, keep unsupported shapes as pre-VM
    declines, and describe Batch 5 memory/profile evidence as allocation
    stability only unless a separate before/after proof justifies a
    performance-improvement claim.
12. When defining property-read production eligibility, keep candidate
    recognition separate from VM acceptance until the same slice adds compiler
    opcodes, VM semantics, route-priority proof, and negative no-route tests.
    Direct named candidates are activation-resolved base reads followed by one
    or more non-optional, non-private `GetNamedProperty` operations; this
    supersedes the older exact two-hop limit from ADR 0222. Direct computed
    candidates must preserve the exact
    ordinary-read lowering sequence:
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
14. When adding compiler helpers that probe an `ExpressionProgram` shape and
    append unified instructions, keep the helper side-effect-free until the
    full shape is accepted. Either prevalidate the whole operation sequence
    before mutating shared instruction/string/literal builders, or emit into
    scratch builders and commit atomically. A helper that returns `false`,
    with or without a decline reason, must not leave partial stack instructions
    or constants behind for the next fallback path. Pair accepted examples with
    adjacent unsupported examples so partial-emission stack drift is caught by
    the focused proof pack.
15. When proving no-route behavior for unsupported property-read-adjacent
    shapes through public invocation logs, assert absence of
    `unified-bytecode-production-fast-path` for the exact function or method
    body that contains the unsupported expression. If the source uses a wrapper
    function to call a class method or nested helper, give the owning method a
    unique name and target that name in the negative assertion. A wrapper-level
    no-route assertion only proves the wrapper was not routed; it does not prove
    the unsupported callee body stayed out of production unified bytecode.
16. When adding property write/update production routing, guard private-name
    strings before treating named property opcodes as ordinary property access.
    Expression bytecode can represent private member reads, writes, and updates
    as named property operations carrying a private-name string; selector scans
    must decline those with `PrivateFieldDependency`, and compiler shape probes
    must reject them before appending `GetNamedProperty`, `SetNamedProperty`,
    or `UpdateNamedProperty` opcodes or string constants. Pair every accepted
    ordinary named write/update widening with negative private read/write/update
    tests. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-define-5cc93efb5a`
    / PR #2379 initially widened ordinary property writes/updates, and review
    found that private-field named-property op shapes were not guarded; the
    build-back fix added selector and compiler guards plus focused
    `receiver.#field`, `receiver.#field = value`, and `receiver.#field++`
    declines.
17. When proving strict-mode property writes or updates on the production
    unified bytecode path, carry lexical strictness into the VM explicitly and
    prove that the strict body actually logs
    `unified-bytecode-production-fast-path`. The production bridge does not
    create the normal function `JsEnvironment`, so property handle resolution
    must not rely only on `context.CurrentScope.IsStrict`. Directive prologue
    support in the compiler must stay no-op and narrow: only string-literal
    `EvaluateAndDiscard` instructions may be skipped to reach the owned return
    payload, while non-directive discarded expressions still decline before VM
    execution. For computed write proofs, keep the admitted write function
    call-free and place unrelated RHS side effects at the call site if needed,
    then assert evaluation order and route logging on the admitted function.
    WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-2-d45261c97e`
    / PR #2396 initially added property-write proof coverage, but review found
    one computed-write proof did not exercise the admitted fast path and the
    strict arm was not proven to route. The build-back fix added directive
    string-literal discard support plus explicit VM strictness threading so
    strict failed writes throw through the owned unified path.
18. When hardening property-write production boundaries, keep logical member
    assignments, dynamic value dependencies, and computed-key expressions with
    unowned payloads as pre-VM declines until the same slice owns selector,
    compiler, VM, and route-proof semantics for those shapes. Pair each
    eligibility decline with public invocation fallback/no-route proof for the
    exact function body. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-3-d4c5a8e668`
    / PR #2415 added focused logical write, dynamic RHS write, and computed key
    expression write coverage after the ordinary property-write boundary was
    admitted. Without those neighboring declines, future property-set widening
    can accidentally route logical assignment, dynamic lookup, or computed-key
    expression payloads through a VM path that does not yet own those semantics.
19. When admitting direct compound property writes into production unified
    bytecode, preserve the reference operands with dedicated get-for-set opcodes
    instead of adding generic stack duplicate/swap opcodes or VM fallback. Named
    compound writes must keep the receiver live for `SetNamedProperty`, and
    computed compound writes must keep both the receiver and the already-resolved
    key live for `SetComputedProperty`. Keep the selector and compiler matched
    to exact operation sequences, and leave logical assignment, nested member
    chains, richer computed keys, optional chains, `super`, private fields,
    `delete`, calls, destructuring, and dynamic lookup as pre-VM declines until
    a later slice owns their full proof. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-4-f0057ffdc4`
    / PR #2426 widened direct named/computed compound writes by adding
    `GetNamedPropertyForCompoundSet` and `GetComputedPropertyForCompoundSet`.
    Those opcodes intentionally avoid treating compound writes as permission for
    a generic expression-stack VM or broad property-write routing.
20. When adding a broad production proof pack for an already-admitted unified
    bytecode family, prove the accepted and rejected boundaries separately. For
    accepted source shapes, assert selector eligibility, `None` decline code,
    required owned opcodes, and an allowed-opcode subset per case so a future
    compiler widening cannot smuggle unowned operations into the route. For
    invocation coverage, assert `unified-bytecode-production-fast-path` on the
    exact newly covered function variants and assert no-route fallback for
    adjacent unsupported bodies such as discarded writes/updates, nested member
    chains, complex compound writes, and destructuring writes. WHY: issue
    `planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-5-59d012f0b5`
    / PR #2438 added the post-boundary baseline proof pack for property
    write/update routing. The useful lesson was that behavior-only tests were
    not enough; accepted mutation shapes also needed explicit owned-opcode
    whitelist proof, while unsupported neighbors still needed public no-route
    proof.
21. Before widening parallel unified-bytecode lanes, start from
    `docs/unified-bytecode-expansion-contract.md` and keep contract, roadmap,
    and ADR/rule surfaces synchronized in the same slice when shared boundary
    text changes. The contract must separate current support from
    reserved/planned lanes, keep the no-mixed-execution rule explicit, and keep
    next unsupported buckets explicit (wider call families; unsupported
    driver-state subshapes: async iterator drivers, TDZ head environments,
    awaited iterator/for-in sources, object destructuring, expression-level
    `ApplyBindingTarget` destructuring, dynamic-name destructuring targets, and
    non-slot/unified-slot failures that still decline before VM execution;
    dynamic lookup) until dedicated ownership slices land. Label-dependent
    control flow is no longer an unsupported bucket: ADR 0285 / issue #2679
    admitted it (see rule #36); only the narrow driver-crossing labeled-abrupt
    residue still declines as `LabelControlFlow`. Keep the drift guard in
    `ExpressionProgramCoverageMapTests` covering required headings plus current
    `UnifiedBytecodeOpCode` and `UnifiedBytecodeProductionDeclineCode` names.
    Treat newly VM-executed literal-construction opcodes such as `CreateArray`,
    `ArrayPush`, `CreateObject`, and `DefineObjectProperty` as current contract
    inventory in the same delivery slice; do not defer them to a learn-stage
    docs pass. WHY:
    issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-25de646b9f`
    / PR #2466 established the shared contract before parallel lane work so
    future agents do not re-discover owner surfaces, imply planned-lane runtime
    support, or land selector/compiler/VM changes without matching proof
    commands. Issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9f49fefe3d`
    / PR #2476 then failed the quality gate after a literal-construction lane
    added current unified opcodes without the matching contract inventory; the
    build-back repair added the missing opcode names before the delivery PR was
    merged. Issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-b7fbd79613`
    / PR #2474 repeated the same risk for primitive opcodes (`TypeOf`,
    `TypeOfIdentifier`, unary operators, `ToString`, and `Pop`), confirming the
    contract inventory is a delivery gate for every VM-executed opcode lane.
    Issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-651d15496c`
    / PR #2508 closed the Batch 5 documentation surface by synchronizing the
    expansion contract, roadmap, ADR 0256, and this rule around explicit next
    unsupported buckets. The durable lesson is that shared boundary wording and
    unsupported-bucket guidance are delivery-slice artifacts, not later cleanup.
    Issue #2574 / PR #2584 then removed stale generic driver-state bucket
    wording from this rule after the roadmap/contract wording had become
    explicit, confirming that adjacent rule text is part of the same
    synchronization boundary.
22. When admitting activation-value loads into production unified bytecode,
    keep them call-time owned by the sync invocation bridge. `LoadThis` and
    `LoadNewTarget` may execute only as owned VM opcodes supplied with
    invocation-local values from `SyncFunctionInvoker`; sloppy `this` must reuse
    the existing non-strict receiver coercion helper before VM execution, and
    ordinary-call `new.target` stays within the existing undefined-newTarget
    production gate. Do not create a `JsEnvironment`, call back into
    `ExpressionProgram`, or infer that `arguments`, arrows, classes,
    constructors, captured/dynamic activation, home/super/private state, or
    non-undefined construct targets are covered by this lane. Keep selector,
    compiler, VM, public route proof, and
    `docs/unified-bytecode-expansion-contract.md` opcode inventory aligned in
    the same slice. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9db8c7189a`
    / PR #2473 admitted `this` and ordinary-call `new.target` through
    production unified bytecode, and the quality gate caught a missing
    `LoadThis` / `LoadNewTarget` expansion-contract inventory update before the
    lane was complete.
23. When admitting primitive unary, conversion, discard, or strict equality
    operations into production unified bytecode, keep the semantics VM-owned and
    activation/TDZ-aware. The compiler may flatten only supported
    `ExpressionProgram` operations into owned opcodes such as `TypeOf`,
    `TypeOfIdentifier`, unary plus/minus/logical-not/bitwise-not/void,
    `ToString`, `Pop`, and explicitly proven strict equality operators. The VM
    must reuse the same runtime helpers as expression-program execution
    (`JsOps.ToNumber`, `TypedAstEvaluator.NegateValue`,
    `TypedAstEvaluator.BitwiseNot`, `JsOps.ToJsString`, and
    `JsOps.StrictEquals`) and check `context.ShouldStopEvaluation` after
    coercive helper calls. `TypeOfIdentifier` may route only for
    activation-resolved names; unresolved `typeof` identifiers still decline as
    dynamic lookup. Production invocation must mark root lexical activation
    slots as `JsValue.Uninitialized` from `ActivationSlotShape` before
    parameter population, and VM `LoadSlot` / `TypeOfIdentifier` must preserve
    TDZ `ReferenceError` behavior with slot names when available.
    `EvaluateAndDiscard` support must compile the supported expression first
    and then append `Pop`, not skip side effects or abrupt completions. WHY:
    issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-b7fbd79613`
    / PR #2474 widened the primitive operator lane, then needed build-back
    repairs for expansion-contract opcode inventory and production lexical TDZ
    reads. The durable lesson is that broad primitive support is safe only when
    selector, compiler, VM semantics, public route proof, dynamic declines,
    TDZ setup, and contract docs move together.
24. When adding unified-bytecode call-target preparation, keep preparation
    bytecode-owned but non-executable for eval/construct and other
    unproven call families until a separate invocation slice owns
    receiver binding, direct-eval, construct/super, optional-call, and spread
    semantics end to end. The #2495 identifier-call slice and #2530 named
    member-call slice plus the #2531 computed member-call slice and the #2538
    receiver-aware integration slice are the narrow exceptions:
    activation-resolved identifier calls, direct named member calls with
    activation-resolved optional-free named receiver chains, and direct
    computed member calls with shallow activation-resolved receiver chains may
    execute through the VM-owned
    `CallInvocationBoundary` when their arguments are simple literal/slot
    operands and computed member keys are also simple literal/slot operands.
    Issue #2676 / PR #2685 widened all admitted shapes to accept **spread
    arguments** (`f(...args)`, `obj.m(...args)`, mixed positional+spread
    `f(a, ...b, c)`): the `CallInvocationBoundary` operand is extended with a
    spread-mask reference (high bits = `SpreadMaskConstantIndex + 1`, zero
    means no spread); the compiler packs the mask and the VM flattens via
    `TypedAstEvaluator.EnumerateSpread` left-to-right before invoking.
    Spread is now a fully owned admitted shape; no new opcode was added and
    no AST/`ExecutionPlanRunner` fallback is used in the spread path.
    Executable identifier calls must still carry the caller
    `EvaluationContext` and active `JsEnvironment` into existing callable
    invocation helpers. Executable named member calls must keep the receiver on
    the stack, load the named callee from that receiver, and preserve the final
    resolved receiver as `this` for the invocation boundary; for accepted nested
    receiver chains such as `root.child.read()`, the final receiver is
    `root.child`, not `root`. Executable computed member calls must keep the
    final receiver on the stack, consume the computed key through the
    context-aware property lookup path, preserve key-conversion and nullish
    receiver ordering, and preserve that receiver as `this` for the invocation
    boundary. If an
    accepted program enters a block lexical scope before invoking the callable,
    the VM must maintain environment owners for the active slots so
    environment-aware and debug-aware callables observe the same scope chain as
    the existing expression-call path. Pair the route proof with regression
    coverage for parameter-passed and block-scoped debug-aware callables for
    identifier calls, final-receiver/`this` preservation for direct named
    member calls, optional/super call declines, and computed-key conversion
    side effects plus nullish receiver and non-callable callee errors for
    computed member calls;
    do not rely only on ordinary JavaScript return values. It is valid for the
    compiler to emit typed `UnifiedBytecodeCallTarget` records and
    `Prepare*CallTarget` opcodes for activation-resolved identifier/member
    calls (including spread-argument forms admitted by #2676), but all
    unproven production call routing must
    decline at `CallInvocationBoundary` and the VM must not call back into
    `ExpressionProgram`, `ExecutionPlanRunner`, AST evaluation, or a generic
    host-call fallback. Update
    `docs/unified-bytecode-expansion-contract.md` in the same slice for every
    new prep opcode or decline code. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-161f73f52d`
    / PR #2479 added the first shared call-target preparation lane, and the
    learn-stage drift guard immediately failed because the expansion contract
    missed `PrepareIdentifierCallTarget`; the durable lesson is that call prep
    can become a reusable bytecode surface only while invocation remains an
    explicit, documented production decline. Issue #2495 / PR #2501 then made
    the first identifier-call slice executable; review found the initial VM
    path invoked parameter-passed `__debug` without the caller environment or
    context, so the accepted repair threads the invocation environment into the
    VM and proves both parameter and block lexical debug-aware calls. Issue
    #2530 / PR #2534 then made the direct named member-call slice executable;
    the durable lesson is that `PrepareNamedCallTarget` may execute only when
    the VM preserves the receiver/callee stack contract and tests prove
    receiver-as-`this` behavior while computed, eval, super/private,
    optional, dynamic, and other unproven call families still decline. Issue
    #2531 / PR #2535 then made the direct computed member-call slice
    executable; the durable lesson is that `PrepareComputedCallTarget` may
    execute only when the VM preserves receiver-as-`this`, computed key
    conversion/order, nullish receiver errors, and non-callable callee errors
    while eval, construct/super, optional, private/super, dynamic,
    complex computed-key, and other unproven call families still decline.
    Issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-cffd4a813a`
    / PR #2538 integrated the named/computed member-call work after those
    slices landed separately. The durable lesson is that receiver-aware
    production calls must bind `this` to the final receiver in the accepted
    chain, keep computed nullish-receiver-before-key-coercion ordering, and
    prove optional and super call families still decline before VM execution.
    Issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-3cea46640b`
    / PR #2609 then widened named member-call receivers to arbitrary
    optional-free named-chain depth while deliberately keeping deeper named
    receivers followed by computed call targets declined. The durable lesson is
    that named-chain depth can widen with existing `GetNamedProperty` and
    `PrepareNamedCallTarget` opcodes, but deeper computed-member call neighbors
    require their own proof slice. Issue #2676 / PR #2685 then admitted
    **spread arguments** across all admitted call shapes by extending the
    `CallInvocationBoundary` operand with a packed spread-mask index (high
    bits = `SpreadMaskConstantIndex + 1`, zero means no spread). The durable
    lesson is that a new argument-passing form can extend the existing call
    boundary operand encoding rather than requiring a new opcode, provided the
    spread flattening reuses a shared helper (`EnumerateSpread`) and the no-mixed-
    execution rule is respected — no AST/IR fallback for the spread path.
    Optional calls, spread-onto-construct, super calls, and direct eval continue
    to decline at their existing guards and are not affected by the spread
    admission. Non-spread plain-constructor calls (`new F(...)`) were admitted
    separately in issue #2690 / PR #2697 (rule #37).
25. When encountering stateful for-in or array-destructuring driver
    instructions in production unified bytecode eligibility, decline before VM
    execution until a full driver-state model is owned. `ForInInitInstruction`
    and `ForInMoveNextInstruction` must decline as
    `ForInDriverStateDependency`; array-destructuring init/element/rest/close
    instructions must decline as `DestructuringDependency`. Do not add partial
    driver-step opcodes, VM callbacks, or `ExpressionProgram` /
    `ExecutionPlanRunner` fallback to make one step executable before selector,
    compiler, VM, state lifecycle, close/abrupt behavior, positive route proof,
    adjacent no-route proof, and expansion-contract inventory all move
    together. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-447731fb5a`
    / PR #2486 found that the unified VM has no owned iterator/destructuring
    driver-state model yet. The delivery added explicit decline codes/tests and
    documented the model-first boundary instead of widening opcodes or VM paths
    opportunistically.
26. When admitting completion and expression-statement behavior into production
    unified bytecode, keep completion effects VM-owned and decline-first.
    Empty or implicit returns should compile to `ReturnUndefined`; non-awaited
    `ThrowInstruction` payloads should compile to owned expression operations
    followed by `Throw`; and VM `Throw` must set `EvaluationContext` throw
    state so caller-side JavaScript `try`/`catch` remains observable. For
    `EvaluateAndDiscard`, compile only supported owned expression operations
    first and then append `Pop`; do not skip side effects, swallow abrupt
    completions, add an eval/fallback opcode, or route through
    `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation. Removing a
    discarded-expression decline is valid only when the underlying operation
    family is already production-owned by selector, compiler, VM, and route
    proof. Keep adjacent call, dynamic lookup, optional-chain, `super`, delete,
    destructuring, for-in driver, and unowned computed-key families as pre-VM
    declines until a separate slice owns them end to end. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-eb801dff13`
    / PR #2488 admitted `ReturnUndefined`, `Throw`, and discarded property
    write/update side effects through production unified bytecode. The useful
    conflict-resolution lesson was to preserve sibling explicit for-in and
    destructuring model-first declines while removing only the redundant
    discarded property write/update veto that current owned VM semantics had
    superseded.
27. When admitting loop-control shapes to production unified bytecode, keep
    target semantics compiler-owned and label decline explicit. Supported
    unlabeled `BreakInstruction` and `ContinueInstruction` cases may compile
    only as resolved `Jump` targets through the same IR-instruction to
    bytecode-PC map used for ordinary jumps. Prove forward breaks, continue
    backedges, for-style update continue targets, and do-while branch
    consequent backedges with selector eligibility and public route-log tests.
    Labeled breakable control flow is now admitted (ADR 0285 / issue #2679, rule
    #36); `LabelControlFlow` no longer blanket-declines labels and now scopes
    only the driver-crossing labeled-abrupt residue. Keep unsupported complex
    loop/control-flow shapes as pre-VM declines.
    After widening compile support, update prototype expectations that used to
    assert old decline behavior so `make quality` catches drift before merge.
    WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-9d6cd3060b`
    / PR #2489 widened production loop-control support. The
    conflict-resolution stage had to preserve main's for-in/destructuring
    declines while repairing stale prototype tests that still expected
    for-loop post-update shapes to fail.
28. When admitting block lexical scopes to production unified bytecode, make the
    accepted program own the full flat-slot span, not just root activation
    slots. Complete root activation mappings even when non-root flat mappings
    are present, derive `UnifiedBytecodeProgram.SlotCount` from every flat-slot
    id, preserve `ParameterSlotIndices` positions with `-1` sentinels for
    unused formals, and resolve scoped `let` / `const` declarations through the
    active scope flat mapping. `PushEnvironment` may stamp lexical TDZ slots as
    `JsValue.Uninitialized` and `PopEnvironment` may remain an owned VM
    cleanup opcode for the admitted linear shape, but neither opcode may create
    a `JsEnvironment`, do name fallback, or call back into
    `ExecutionPlanRunner`, `ExpressionProgram`, or AST evaluation. Keep
    `BindingVariableDeclaration`, destructuring, `with`, direct eval / dynamic
    lookup, captured activation, per-iteration bindings, and `using`
    declarations declined until a later slice owns those semantics end to end.
    WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-2f1dcc1cd5`
    / PR #2490 widened the block lexical-scope lane, and the quality gate found
    that partial root remapping plus non-root flat mappings broke unrelated
    parameter-population paths. The accepted repair made slot layout
    program-wide and positional instead of activation-only or name-based.
29. After parallel unified-bytecode lanes have individually widened production
    support, prove they compose as one accepted production boundary before
    treating the batch as coherent. The integrated selector proof should combine
    only already-owned families, assert `None` decline code, assert required
    owned opcodes, and assert absence of non-executable call-target preparation
    or invocation-boundary opcodes. The matching public invocation proof should
    assert `unified-bytecode-production-fast-path` on the same function and
    expected JavaScript result. Keep direct specialized simple-return binary and
    binary-chain shortcuts ahead of unified bytecode and assert that those
    functions do not log the unified route. Do not add VM fallback or broaden
    adjacent unowned families to make an integrated test pass. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-fc03ae9db9`
    / PR #2503 closed the production-routing integration slice with a guard-only
    proof pack. The durable lesson was that per-lane acceptance is not enough:
    completed lanes must compose inside one VM-owned program while route
    priority still protects older specialized fast paths.
30. Keep production unified-bytecode no-mixed-execution source gates both
    method-boundary-scoped and VM-scoped. The sync-invoker source gate should
    scan only `TryInvokeProductionUnifiedBytecode<TArgs>(...)`, because the
    surrounding invoker file legitimately owns fallback paths. The same test
    must also scan `UnifiedBytecodeVirtualMachine.cs` and forbid
    `ExecutionPlanRunner`, `ExpressionProgram`, `EvaluateExpression(`,
    `ProfileEvaluateExpression(`, and `EvaluateDynamicExpressionProgram(`.
    A gate that omits `ExpressionProgram` from the VM source can pass while
    leaving the mixed-expression bridge open. Repository-root discovery for
    source gates must work in normal checkouts and linked worktrees: accept a
    `.git` directory, a `.git` file, or the solution marker together with the
    expected `src/Asynkron.JsEngine` tree. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-a88c0a9ba1`
    / PR #2509 hardened the accepted-path proof after review caught two
    guardrail holes: linked-worktree root discovery and a missing VM-source
    `ExpressionProgram` assertion.
31. Before creating or rewriting follow-on Faktorial plans for unified
    bytecode, rebase lane order against current local `main`,
    `docs/unified-bytecode-expansion-contract.md`, relevant ADR/rule
    boundaries, and recent merged production proof. Do not carry a stale first
    slice forward just because an earlier plan or ADR named it. If the planned
    first slice is now current support, name it as baseline/already landed,
    move the first still-unsupported family to Batch 1, and update the ADR and
    Faktorial plan body together. WHY: issue
    `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-833518a41c`
    / PR #2515 found the follow-on plan still started at direct identifier
    calls even though issue #2495 / PR #2501 had made no-spread
    activation-resolved identifier calls executable. The accepted correction
    rewrote ADR 0261 and plan `planmanual1779961785446650000` so named member
    calls became the first remaining lane, computed member calls second, and
    constructor/super, spread/direct eval, dynamic lookup,
    iterator/destructuring, and label families stayed deferred. Issue #2530 /
    PR #2534 has since landed named member calls, issue #2531 / PR #2535 has
    since landed direct computed member calls, issue #2679 / PR #2683 has
    since admitted labeled control flow (rule #36), and issue #2690 / PR #2697
    has since admitted non-spread plain-constructor calls (rule #37). Future
    plan edits must treat spread-onto-construct, super calls, member-target
    constructs, direct eval, dynamic lookup, and iterator/destructuring as
    remaining lanes unless current `main` proves an even newer slice has
    landed.
32. When preserving or widening with-backed dynamic names on the production
    unified bytecode route, keep the accepted program activation-hoist aligned
    and receiver-owned. The sync bridge must define function-scoped var bindings
    in the fast activation environment before VM execution so nested callees
    called from inside an outer `with` still see their own hoisted var names as
    `undefined` before any initializer runs. VM
    `PrepareDynamicIdentifierCallTarget` must resolve active with bindings
    regardless of identifier-cache state and must push the with binding object
    as the receiver when the identifier comes from that object. Keep direct
    eval source execution, captured dynamic activation, arguments objects,
    async/generator functions, and unresolved non-with dynamic lookup as
    pre-VM declines. Pair retained changes with the focused
    `Statements_with` Test262 row and public production invocation tests for
    both hoisted-var shadowing and with-object receiver binding. WHY: issue
    #2564 / PR #2571 fixed `S12.10_A1.11_T5` after the with-backed production
    route failed to create a nested function's hoisted local `value` binding
    before dynamic lookup and dynamic identifier call preparation still depended
    on the identifier-cache path.
33. When admitting `try/catch/finally` exception regions to production unified
    bytecode, keep exception routing and abrupt-completion propagation
    descriptor-backed and VM-owned. `EnterTry`, `EnterCatch`, `LeaveTry`, and
    `EndFinally` must compile to owned opcodes with `UnifiedBytecodeTryDescriptor`
    and `UnifiedBytecodeCatchDescriptor` payloads; the VM must own `TryFrame`,
    `PendingCompletion`, catch binding activation/inactivation, and finally
    replacement semantics without calling back into `ExecutionPlanRunner`,
    `ExpressionProgram`, or AST evaluation. For loop control through finally,
    compare compiled driver descriptor topology and mapped break/continue
    targets; do not decide whether to schedule an outer synthetic for-of finally
    from currently active driver-state slots alone, because an inner iterator can
    already be closed when its pending break reaches an outer frame. Pair the
    route proof with catch binding leak/direct-read regressions, return/throw
    replacement through finally, break/continue through finally, nested for-of
    inner-break cleanup ordering, and unsupported async/generator/dynamic
    declines. WHY: issue
    `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-0bfc08d573`
    / PR #2591 admitted ordinary synchronous exception regions, then build-back
    fixes exposed catch-slot lifetime, operand-stack cleanup, pending body-throw
    preservation, and nested driver-cleanup topology as the durable guardrails.

34. When removing a pre-gate from `CanUseProductionUnifiedBytecodeFastPath`,
    verify that the plan-level decline taxonomy in
    `UnifiedBytecodeProductionEligibility.TryFindExpressionDecline` already
    covers every opcode family the pre-gate was blocking; do not remove a gate
    whose declined case has no corresponding plan-level boundary. The
    `_homeObject is not null` pre-gate is the concrete reference: all super-
    property opcode families (`GetNamedSuperProperty`, `GetComputedSuperProperty`,
    `SetNamedSuperProperty`, `SetComputedSuperProperty`,
    `UpdateNamedSuperProperty`, `UpdateComputedSuperProperty`,
    `EnsureSuperReference`) were already declined by `SuperPropertyDependency`,
    making the gate redundant. The remaining pre-gates
    (`_lexicalThisEnvironment is not null`, `_superConstructor is not null`,
    `_superPrototype is not null`) correspond to capability gaps the plan-level
    taxonomy does not fully cover and must not be removed without a matching
    plan-level decline or proven VM support. When admitting `this`-using class
    or object-literal methods through the fast path, note that the property-
    write boundary still applies: simple `this.prop = slot/constant` assignments
    are within boundary, but compound read-modify-write patterns such as
    `this.prop = this.prop + n` are declined by the existing
    `PropertyWriteDependency` rule and must not be used as fast-path invocation
    proof in tests. When writing invocation proof tests for class methods or
    object-literal methods, assert `func=<anonymous>` in the production log — not
    the JavaScript method name — because class method AST nodes carry no `Name`
    field; asserting the JS identifier will silently pass the wrong log line or
    fail on the correct one. WHY: issue #2633 / PR #2643 found that removing the
    `_homeObject` pre-gate was safe exactly because `SuperPropertyDependency`
    already covered every super opcode family; the build-review process then
    surfaced the `func=<anonymous>` naming trap and the `PropertyWriteDependency`
    boundary as two further durable guardrails for future `this`-using method
    admissions.

35. When admitting `this`-dependent async/generator functions to the
    **resumable** unified bytecode route, clear both gates and own the `this`
    lifetime on resume state. The resumable route declines `this` in two
    independent places, and both must move together: remove the
    `HasThisDependency` decline from
    `UnifiedBytecodeProductionEligibility.EvaluateResumable`, AND add
    `UnifiedBytecodeOpCode.LoadThis` to the resumable-supported opcode set in
    `TryFindUnsupportedResumableOpcode`. (The async invoker sets
    `HasThisDependency`, so the eligibility decline blocks async methods with a
    receiver; the sync generator invoker never sets it, so the opcode allowlist
    is the only gate keeping `this`-using generators safe — clearing one without
    the other leaves a half-open boundary.) `this` must live on the long-lived
    `UnifiedBytecodeResumeState` (captured at construction, alongside slots /
    operand stack / program counter), NOT as a per-step VM parameter like the
    non-resumable `Execute`, because it has to survive suspension/resume across
    `yield`/`await`. Add a `case LoadThis:` to `ExecuteResumable` that pushes
    `state.ThisValue`, mirroring the non-resumable `Execute` `LoadThis`. Coerce
    in the invoker before VM entry via the shared static
    `CoerceThisValueForNonStrict` (promoted out of `SyncFunctionInvoker`) so
    strict/sloppy `this` is byte-for-byte identical to the sync route, and the
    resumable `LoadThis` always loads the pre-coerced value. Keep
    `LoadNewTarget` out of the resumable allowlist and keep new.target,
    captured/dynamic activation, arguments-object, call, dynamic-lookup, and
    async-generator shapes declining before VM execution. Only bare `this`
    flowing through resumable-supported opcodes (`LoadThis`, `Yield`, `Binary`,
    `Return`) is admitted; `this.x` property reads and `typeof this` stay
    outside the resumable opcode set and decline independently. Prove
    strict/sloppy primitive `this` fidelity, `this` after both `yield` and
    `await` suspension, and adjacent new.target / arguments-object declines
    (async + generator). WHY: issue #2675 / PR #2680 widened the resumable route
    as the counterpart to the sync `this` support (rule #34, #2633/#2643). The
    durable lesson is that the resumable route's two-gate structure
    (eligibility-decline + opcode-allowlist) and the resume-state `this`
    lifetime are different from the sync route's single pre-gate; a future
    resumable widening (e.g. `this.x` reads, new.target) must clear both gates
    and pick the resume-state lifetime, not the per-step VM-parameter lifetime.

36. When admitting label-dependent control flow to production unified bytecode,
    treat labels as compiler-owned targets, not a source-syntax permission, and
    bound the admission by the VM's single-level driver cleanup. There are two
    blanket gates and both move together: remove the
    `BreakableEnterInstruction { Label: not null }` → `LabelControlFlow` decline
    in `UnifiedBytecodeProductionEligibility`, AND relax the compiler's
    `IsSupportedBreakableEnter` (plus `HasLoopContinueTarget` for labeled
    loop continue/break metadata) so labeled breakable enters accept. A labeled
    construct routes whenever its *unlabeled* IR topology would route — the
    canonical-loop topology checks (condition-first backedge, for-style update
    continue, do-while consequent, single-pass driver loops) still gate which
    shapes are eligible. Labels resolve to numeric targets in the plan builder
    (`ControlFlowEmitter`), so the compiler already sees a fully resolved jump
    target through `TryAppendResolvedJump` regardless of label presence; the VM
    needs no new opcode. The correctness boundary is **driver cleanup**: the VM
    closes only the single driver whose descriptor `BreakTarget` equals the
    abrupt jump target (`CleanupDriverStatesForBreakTarget`). A labeled
    `break`/`continue` that exits *several* nested iterator/for-in driver loops
    at once would leak the intervening inner iterators. So keep the VM
    fallback-free and single-level, and decline before VM execution the one
    shape it cannot serve — a labeled abrupt that crosses an enclosing driver
    loop it is not directly targeting — via per-driver structured body-region
    analysis (`IsLabeledAbruptCrossingDriver`), still reusing the
    `LabelControlFlow` decline code (now scoped to this residue, not all
    labels). Do **not** substitute a program-counter ordering heuristic for
    multi-driver cleanup: it was prototyped and empirically rejected here
    because the compiler's lazy target compilation does not guarantee an inner
    loop's exit PC precedes its enclosing loop's exit PC, so PC-ordering closed
    the wrong driver set (outer closed, inner leaked). Multi-driver labeled
    cleanup is the next loop-control frontier and needs nesting metadata on
    driver descriptors, not a PC heuristic. Move stale prototype decline
    expectations in the same delivery (labeled while/non-loop `TryCompile` cases
    now assert success; the `UnsupportedControlFlowFunctions` labeled entry moves
    to `SupportedLoopControlFunctions`) so `make quality` stays meaningful, and
    sync the expansion contract bucket #5 + Production Loop-Control Boundary and
    roadmap in the same slice (rule #21). Pair proof: selector acceptance for
    labeled loop / labeled break-out-of-for-of / labeled single-loop continue,
    labeled block/loop route-log invocation proofs, driver-close-on-labeled-break
    proof, and negative driver-crossing decline proofs for both labeled `break`
    and labeled `continue`. WHY: issue #2679 / PR #2683 cleared the contract's
    "Ranked Next Unsupported Buckets" #5. The durable lesson is twofold: (1)
    label decline was a source-syntax veto layered over an already-resolved
    numeric-target path, so admitting it is mostly removing two gates plus one
    narrow soundness decline; (2) the single-level VM cleanup model — not the
    label itself — is the real boundary, and a PC-based multi-driver shortcut is
    unsound under lazy target compilation.

37. When admitting synchronous non-spread construct calls (`new F(...)`) to
    production unified bytecode, keep the `ConstructInvocationBoundary` opcode
    receiver-free and mirror the spec-conformant construct reference helper
    (`ExecuteProgramConstruct`). The construct model differs from call: the
    constructor is pushed as a plain value load with no receiver or call-target
    preparation, and `[[Construct]]` is invoked with the constructor as both the
    target and `new.target`. Spread-onto-construct (`new F(...args)`) must
    decline as `ObjectLiteralOrSpreadDependency`; member-target constructs
    (`new obj.F()`) and non-simple argument expressions must decline at their
    existing guards, matching the parallel restrictions on call targets. Keep the
    super call family (`SuperConstruct`, `LoadNamedSuperCallTarget`,
    `LoadComputedSuperCallTarget`) explicitly declined as
    `SuperPropertyDependency` — do not implement them in the flat-slot VM. The
    activation gate in `SyncFunctionInvoker.CanUseProductionUnifiedBytecode`
    already blocks derived class constructors (`IsClassConstructor`,
    `_superConstructor`, `_lexicalThisEnvironment`, `newTarget`, etc.) before
    expression eligibility runs, making any `SuperConstruct` expression
    unreachable in production. Implementing the ~170-line super construction
    machinery (super binding resolution, `ThisInitialized` guard,
    `MarkThisInitialized`, class-field initializers) in the VM would be
    untestable, unprovable dead code — a direct contradiction of the proof-pack
    requirement that every admitted shape be demonstrable. The principle is:
    **activation-gate unreachability is a proof-pack blocker**. If the
    activation layer already prevents a function kind from routing through
    unified bytecode, implementing VM semantics for ops that appear only in those
    function kinds cannot be proven through the normal proof pack and must not be
    added until the activation gate itself is widened and the matching VM
    semantics are demonstrable. Pair construct admission with proof-pack
    coverage for `new.target` propagation, zero/many-arg construct, argument
    order, not-a-constructor `TypeError`, and the spread/member-target/super
    negative declines. WHY: issue #2690 / PR #2697 admitted `Construct` with ADR
    0286. The super family analysis confirmed that gate-layer decisions upstream
    of expression eligibility determine what can be demonstrably proven in the
    flat-slot VM; future widening of the activation gate must pair with matching
    VM semantics proof before the expression-level decline is removed.

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

Issue #2314 / PR #2320 widened the executable property-read boundary to the
exact two-hop direct named chain `LoadIdentifier -> GetNamedProperty ->
GetNamedProperty`. The durable lesson is two-part: the boundary itself stays
small and owned by existing `GetNamedProperty` opcodes, and shape-probing
compiler helpers must not partially mutate shared builders before a full shape
match is known. WHY: focused verification first exposed stack corruption from
partial emission in the new named-chain helper; the accepted fix prevalidated
the full chain before emitting `LoadSlot` and property-read opcodes. Issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-3cea46640b`
/ PR #2609 later removed the exact two-hop cap for optional-free
activation-resolved named chains by reusing existing `GetNamedProperty`
emission. The durable lesson is that deeper named reads are acceptable only
when every hop remains VM-owned and adjacent computed/optional/private/dynamic
families keep explicit pre-VM declines.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-3-com-ca10aa7559`
and PR #2321 completed the property-read boundary proof by adding explicit
declines for unsupported computed property-key payloads: `box[{ value: 1 }]`
and `box[{ ...source }]`. WHY: object literal/spread operations can appear in
the key-evaluation payload before the final computed property read. Without
source examples for those payloads, future widening can misclassify them as a
generic property-read boundary miss or lose the guardrail while merging adjacent
property-read batches.

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-4-pub-8e90a024bf`
and PR #2329 expanded public invocation route proof for production property
reads. Review caught that the first `super.value` no-route case asserted against
the wrapper function `readSuper`, while the unsupported `super.value`
expression lived inside the derived method. The repair renamed that method to
`readViaSuperBoundary` and asserted no route for the owning method instead.
WHY: a wrapper can stay off the fast path while its callee incorrectly routes,
so negative public-log proof must target the body that actually owns the
unsupported expression.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-define-5cc93efb5a`
and PR #2379 widened production routing to the first ordinary property
write/update boundary. Review found that private-field named-property op shapes
needed explicit guards before the selector or compiler treated them as ordinary
named properties. WHY: non-computed private member access can lower into named
property read/write/update operations with private-name strings, but the unified
VM's ordinary named property opcodes do not own private-name lexical resolution,
brand checks, or private accessor behavior. Future property write/update
widening must decline private names before VM execution unless a separate slice
adds owned private-field semantics.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-2-d45261c97e`
and PR #2396 completed the first ordinary property-write production proof by
closing two review gaps. The computed-write proof had to use a call-free,
route-eligible `write(box, key, value)` body and move RHS side effects to the
call site, because call payloads are outside the admitted boundary. The strict
failed-write proof also had to show the strict function itself used
`unified-bytecode-production-fast-path`. WHY: the unified VM executed accepted
property writes without a function environment, so `context.CurrentScope`
strictness alone was insufficient. The accepted repair passes lexical
strictness from `SyncFunctionInvoker` into `UnifiedBytecodeVirtualMachine` and
lets the compiler skip only directive string-literal discard instructions for
strict directive prologues.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-3-d4c5a8e668`
and PR #2415 added boundary-decline coverage around the admitted ordinary
property-write slice. Logical member assignment, a dynamic/global RHS
dependency, and computed-key expression payloads all execute correctly through
fallback paths and must not log `unified-bytecode-production-fast-path` until a
future slice owns their selector, compiler, VM, and route-proof semantics. WHY:
the admitted simple property-set route is intentionally narrower than the full
property-write family, so nearby write shapes need explicit decline examples to
stop future agents from treating "property write" as a broad source-syntax
permission.

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-4-f0057ffdc4`
and PR #2426 admitted direct named and computed compound property assignments
by adding compound get-for-set opcodes rather than generic unified stack
operators. WHY: compound assignment lowering needs to read the old property
value while preserving the receiver, and computed compound assignment also must
preserve the once-resolved key for the eventual set. A generic duplicate/swap
surface or VM callback would have widened production unified bytecode beyond
the proven selector, compiler, VM, and route-proof boundary.

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-fc03ae9db9`
and PR #2503 closed the parallel-lane production-routing integration batch with
tests rather than runtime widening. The lesson is that the accepted surface must
be proven both lane-by-lane and as an ordinary mixed program: literals, property
reads/writes/updates, block lexical scopes, loop control, and primitive
operations should compose as one `UnifiedBytecodeProgram` without non-executable
call-boundary opcodes or fallback. The same slice also proved binary-chain
simple returns still use their specialized fast path. WHY: without an
integrated guard, future agents can have complete-looking per-lane coverage
while an ordinary sync function either drifts into mixed execution or shadows a
faster established route.

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-a88c0a9ba1`
and PR #2509 made the production unified-bytecode source gate persistent. The
lesson is that no-mixed-execution proof needs two precise scan scopes: the
accepted sync-invoker method body, not the whole mixed-responsibility invoker
file, and the unified VM source itself. Review feedback also showed that
worktree-aware root discovery belongs in the gate, and that `ExpressionProgram`
must be forbidden in the VM source alongside `ExecutionPlanRunner` and AST-eval
entry points. WHY: otherwise a guard can look complete while either failing in
linked worktrees or allowing an expression-bytecode bridge back into the
production unified VM.

Faktorial issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-6700005263`
and PR #2613 added the first production resumable unified-bytecode route for
simple async functions and sync generators. Future resumable widening must keep
this route separate, decline-first, and VM-state-owned: accepted programs must
preserve program counter, operand stack, slots, pending await promise, pending
resume payload, and completion state in `UnifiedBytecodeVirtualMachine`, with
positive route-log proof and adjacent no-route/fallback proof. Do not treat
`YieldStar` opcode presence as production support. `yield*` must continue to
decline before resumable VM execution until delegated `.return()` and
`.throw()` resume behavior is modeled and proven in the VM; observable
delegated abrupt-resume behavior stays on the existing IR generator path until
then. WHY: the build-stage repair for PR #2613 had to decline `yield*` from the
new resumable fast path after the first slice exposed that delegated abrupt
resume is a separate protocol boundary from simple `yield` and awaited return.

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
- `docs/adrs/0224-keep-unified-bytecode-shape-probes-side-effect-free-before-emission.md`
- `docs/adrs/0231-keep-unified-bytecode-property-write-private-names-guarded.md`
- `docs/adrs/0234-keep-unified-bytecode-property-writes-strict-and-directive-owned.md`
- `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`
- `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- `docs/adrs/0247-keep-unified-bytecode-activation-value-loads-call-time-owned.md`
- `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- `docs/adrs/0252-keep-unified-bytecode-completion-lane-vm-owned.md`
- `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- `docs/adrs/0255-keep-unified-bytecode-block-lexical-scopes-program-slot-owned.md`
- `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
- `docs/adrs/0271-keep-unified-bytecode-exception-regions-vm-owned-and-driver-cleanup-topology-guarded.md`
- `docs/adrs/0277-keep-resumable-unified-bytecode-state-bounded-and-yield-star-declined.md`
- `docs/adrs/0279-accept-this-dependent-ordinary-sync-in-unified-bytecode.md`
- `docs/adrs/0283-accept-this-dependent-async-generator-in-resumable-unified-bytecode.md`
- `docs/adrs/0285-admit-labeled-control-flow-in-unified-bytecode-and-decline-driver-crossing.md`
- `docs/adrs/0286-accept-unified-bytecode-construct-calls-and-decline-super-calls.md`
