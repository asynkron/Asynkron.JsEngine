# ADR 0271: Keep unified bytecode exception regions VM-owned and driver-cleanup topology-guarded

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-0bfc08d573`
and PR #2591 widened production unified bytecode so ordinary synchronous
`try/catch`, `try/finally`, and `try/catch/finally` bodies can execute through
`UnifiedBytecodeVirtualMachine` when their contained operations are otherwise
owned.

Before this delivery, the unified compiler mostly treated exception-region IR
as unsupported production surface, except for narrow iterator-cleanup
`try/finally` shapes. The accepted lane added owned `EnterTry`, `EnterCatch`,
`LeaveTry`, and `EndFinally` opcodes, `UnifiedBytecodeTryDescriptor` and
`UnifiedBytecodeCatchDescriptor` metadata, catch binding slot state, and VM
`TryFrame` / `PendingCompletion` handling.

The review/build-back sequence exposed the risky parts of the ownership
boundary:

- catch binding slots must become inactive after leaving catch, so a direct
  read behaves like a `ReferenceError` while `typeof` remains non-throwing;
- returns through nested finally blocks must keep the VM operand stack clean;
- iterator `return()` throws must not replace an already pending body throw
  during finally cleanup;
- nested `for..of` inner breaks must close the inner iterator before the return
  expression while leaving the outer iterator open until the outer loop exits.

That final nested-loop repair showed that active driver-state slots are not a
sufficient topology oracle. The inner iterator can already be closed when its
pending break reaches an outer synthetic for-of finally frame, so the VM must
compare compiled driver descriptors and break targets rather than infer cleanup
from currently active driver state alone.

## Decision

Keep production unified-bytecode exception regions VM-owned, descriptor-backed,
and fallback-free.

- Compile exception-region IR into owned `EnterTry`, `EnterCatch`, `LeaveTry`,
  and `EndFinally` opcodes plus immutable try/catch descriptors. Do not satisfy
  ordinary try/catch/finally support with callbacks into `ExecutionPlanRunner`,
  `ExpressionProgram`, or AST evaluation.
- Keep try-frame state in the VM. A pending completion records kind, value or
  target, resume target, and whether it originated in finally. A normal
  completion schedules finally with a resume target; abrupt return/throw/break
  and continue schedule finally with the pending completion.
- Preserve JavaScript replacement rules: an abrupt completion raised inside
  finally replaces the pending completion from the try/catch body, while normal
  finally completion resumes the saved completion.
- Route throws to catch only before the frame has scheduled finally. Throws
  raised by a scheduled finally block must continue outward rather than being
  caught by the same frame.
- Keep catch binding environments and flat slots aligned. Mark catch binding
  slots inactive after the catch scope leaves so later direct slot reads throw
  through the VM-owned ReferenceError path.
- For loop control through finally, distinguish same-loop continue, current-loop
  break, and inner-loop break using compiled driver descriptor topology and
  mapped control targets. Do not infer outer finally scheduling solely from
  active driver-state slots.
- Keep unsupported async, generator, captured/dynamic activation, unowned
  dynamic lookup, labels, and unproven driver subshapes as pre-VM declines.

## Consequences

- Future exception-region widening must move selector eligibility, compiler
  descriptors, VM completion semantics, catch binding behavior, route proof,
  no-route proof for unsupported neighbors, expansion-contract inventory, and
  no-mixed-execution source gates together.
- Iterator cleanup and exception-region proof must include nested loop
  topologies, not only single-loop break/continue cases.
- Review fixes in this area should preserve the no-mixed-execution boundary.
  A failed try/finally edge case is a VM completion-state or descriptor-topology
  bug until proven otherwise, not a reason to add runner fallback.

## Evidence

- PR #2591 merged as squash commit
  `0852f452b26972f4646db1ec5b5318726fa714a8`.
- Delivery-branch final repair commit:
  `df0798ad39eba2b8d5e368608035af2a1b6dd3a0` ("Fix unified for-of nested break
  cleanup").
- The final run-quality gate passed after that repair.
- Build-stage proof recorded:
  - `rtk git diff --check origin/main...HEAD` passed.
  - AST-eval seam scan over
    `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*` had no
    matches.
  - Focused try/catch/finally plus nested for-of pack passed 10 tests.
  - `UnifiedBytecodeProductionEligibilityTests` passed 124 tests.
- The accepted public route proof includes production fast-path tests for
  `try/catch`, catch binding inactivity, return/throw replacement through
  finally, break/continue through finally, and nested for-of inner-break cleanup
  ordering.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0252: `docs/adrs/0252-keep-unified-bytecode-completion-lane-vm-owned.md`
- ADR 0253: `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- ADR 0261: `docs/adrs/0261-keep-unified-bytecode-call-invocation-boundary-plan-sliced-and-deferred.md`
- ADR 0269: `docs/adrs/0269-keep-with-backed-unified-bytecode-dynamic-names-activation-hoist-and-receiver-owned.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
