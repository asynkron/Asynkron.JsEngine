# ADR 0277: Keep resumable unified bytecode state bounded and yield-star declined

## Status

Accepted

Supersession note: Faktorial issue
`planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-dc74ab36e4`
and PR #2948 later narrowed this ADR. The bounded resumable-state and
no-mixed-execution decisions still stand, but the `yield*` decline is no longer
blanket: sync-generator `yield*` is admitted when the VM owns delegated
`.next(value)`, `.return(value)`, and `.throw(value)` behavior. Async-generator
`yield*`, awaited delegated sources, and unsupported delegated expression
payloads remain outside production resumable routing until separately modeled.

## Context

Faktorial issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-6700005263`
and PR #2613 added the first production resumable unified-bytecode route for
simple async functions and sync generators.

The delivery introduced a VM-owned resume-state model for accepted programs:
program counter, operand stack, slots, pending await promise, pending resume
payload, and completion state survive suspension and resume through
`UnifiedBytecodeVirtualMachine.ExecuteResumable`. The accepted route covers the
first narrow opcode family: `Yield`, `StoreResumeValue`, `AwaitAndDiscard`,
and `AwaitedReturn`, plus ordinary already-owned sync opcodes needed by the
admitted bodies.

The same delivery initially needed a conservative repair around `yield*`.
Although the compiler and VM contain prototype-shaped `YieldStar` mechanics,
production resumable routing cannot safely execute delegated `.return()` and
`.throw()` until that delegated abrupt-resume protocol is modeled end to end.
The existing IR runner already owns those observable semantics, including the
older split in ADR 0085 for delegated return completion.

## Decision

Keep production resumable unified bytecode as a separate decline-first route.

- Admit only async-like or generator activations that satisfy the explicit
  resumable eligibility scan.
- Accepted resumable programs must execute through the unified VM state model;
  they must not call back into `ExecutionPlanRunner`, `ExpressionProgram`, or
  AST evaluators after acceptance.
- Keep captured/dynamic activation, arguments-object dependency, `this`,
  `new.target`, calls, dynamic lookup, labels, iterator/destructuring drivers,
  unsupported expression payloads, and unmodeled statement families as pre-VM
  declines.
- Keep `yield*` as a pre-VM resumable decline until delegated `.return()` and
  `.throw()` resume behavior is VM-owned with positive route proof and adjacent
  fallback/no-route proof. PR #2948 is the sync-generator narrowing of this
  point; the remaining async-generator and awaited-delegate lanes still follow
  the original decline-first rule.
- Until a given `yield*` lane is widened, observable delegated `.return()` and
  `.throw()` behavior must continue through the existing IR generator path.

## Consequences

- The first async/generator bytecode model is a real resumable VM state model,
  not a second interpreter and not a mixed-execution fallback.
- `YieldStar` may appear in the opcode inventory before every lane is admitted,
  but production resumable eligibility must reject any lane whose delegated
  resume protocol is not VM-owned.
- Future resumable widening must move selector, compiler, VM state semantics,
  direct VM/prototype tests, public invocation route-log tests, negative
  fallback/no-route tests, expansion-contract text, and AST seam scans in the
  same slice.
- Do not use simple `yield*` value delegation as proof that delegated abrupt
  resume is owned; `.return()` and `.throw()` are the boundary.
- No CPU or allocation improvement is implied by this ADR. Performance claims
  need current profile evidence separate from route correctness.

## Evidence

- PR #2613 merged as commit `834ab500`.
- Build-stage repair commit `0ab6d9c5` declined production resumable `yield*`
  until delegated abrupt resume is modeled.
- PR #2948 / commit `9d984e764` admitted sync-generator `yield*` after
  `ExecuteResumable` delegated `.return(value)` and `.throw(value)` through the
  underlying iterator, while keeping async-generator `yield*` and awaited
  delegated sources declined in the expansion contract.
- Issue #2955 added focused async-generator `yield*` decline proof: delegated
  async-generator `.return(value)` and `.throw(value)` stay correct through the
  existing IR async-generator path and do not log a resumable unified-bytecode
  fast path until an async-generator VM bridge owns promise queueing and
  async-iterator settlement.
- Focused sync-generator widening proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests&FullyQualifiedName~EvaluateResumable_YieldStar"`
  with 1 test passing, and
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests&FullyQualifiedName~SimpleGeneratorYieldStar"`
  with 3 tests passing.
- Focused repair proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.EvaluateResumable_YieldStar_DeclinesUntilDelegatedAbruptResumeIsModeled|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.SimpleGeneratorYieldStar_DeclinesResumableUnifiedBytecodeFastPath|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.SimpleGeneratorYieldStar_ReturnDelegatesThroughIrAfterResumableDecline|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.SimpleGeneratorYieldStar_ThrowDelegatesThroughIrAfterResumableDecline" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 4 tests passing.
- Focused production pack passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodePrototypeTests|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 312 tests passing.
- AST-eval seam scan found no matches:
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0085: `docs/adrs/0085-keep-yield-star-delegated-return-completion-split.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0258: `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
