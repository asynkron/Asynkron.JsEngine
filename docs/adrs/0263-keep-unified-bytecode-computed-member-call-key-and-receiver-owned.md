# ADR 0263: Keep unified bytecode computed member calls key and receiver-owned

## Status

Accepted

## Context

Issue #2531 / PR #2535 widened production unified bytecode from executable
identifier calls and direct named member calls to direct computed member calls
such as `box[key](value)`.

The compiler already emitted `PrepareComputedCallTarget` for computed member
call-target preparation, but production execution still treated that opcode as
non-executable. That made computed member calls a visible call-prep surface
without a VM-owned invocation lane.

Computed member calls add one observable requirement beyond named member calls:
the VM must preserve both the receiver binding and property-key semantics. The
receiver must remain the call `this` value, while the computed key must be
consumed in JavaScript order so key conversion side effects, nullish receiver
errors, and non-callable callee errors match the existing expression-call path.

## Decision

Allow only direct computed member calls whose receiver chain is
activation-resolved, whose computed key is a simple literal or activation-slot
operand, and whose arguments are simple literal or activation-slot operands to
execute through production unified bytecode.

The VM owns `PrepareComputedCallTarget` for this accepted shape:

1. keep the receiver on the stack;
2. consume the computed key operand from the stack;
3. check the receiver and perform context-aware property lookup through the
   existing `JsOps` helper path;
4. preserve observable property-key conversion behavior and abrupt completion
   propagation through the active `EvaluationContext`;
5. push the loaded callee after the receiver so `CallInvocationBoundary`
   consumes the existing `[receiver, callee, args...]` stack contract; and
6. invoke through the existing callable helpers without calling back into
   `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation.

Production eligibility remains narrower than compiler coverage. Computed key
expressions, spread calls, direct eval, construct/super calls, optional calls,
private/super targets, arguments-object dependencies, dynamic lookup, and
receiver/key shapes that are not activation-resolved remain pre-VM declines
until a separate selector/compiler/VM/proof slice owns them.

## Consequences

- `PrepareComputedCallTarget` is no longer categorically non-executable, but
  only the direct computed member-call shape admitted by issue #2531 may route
  through production.
- Receiver binding and key conversion order are part of the production
  bytecode contract. Future computed-call widening must prove both, not only
  final return values or selector eligibility.
- Complex computed keys, direct eval, spread, construct/super, optional calls,
  private/super member targets, and dynamic lookup remain separate deferred
  lanes.

## Evidence

- PR #2535 merged as commit
  `fcdf53a80a4d231b9931e9ddee1e3f2288c6afbc`.
- Build-stage delivery commit was `ce83738f` on
  `agent-go/task-gh2531` before the squash merge.
- Focused production eligibility/invocation proof passed with 200 tests:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`.
- The AST-eval seam scan reported no matches:
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
- The allocation stability check completed:
  `rtk ./tools/profile forloop --memory` with total allocated 6.73 MB.
- `rtk git diff --check` was clean.

## Related

- Issue #2531
- PR #2535
- ADR 0250:
  `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0261:
  `docs/adrs/0261-keep-unified-bytecode-call-invocation-boundary-plan-sliced-and-deferred.md`
- ADR 0262:
  `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
