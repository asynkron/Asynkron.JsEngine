# ADR 0262: Keep unified bytecode named member calls receiver-owned

## Status

Accepted

## Context

Issue #2530 / PR #2534 widened production unified bytecode from the first
executable identifier-call lane to direct named member calls such as
`box.read(value)`.

The compiler already emitted `PrepareNamedCallTarget` for the narrow direct
member-call shape. Before this slice, production eligibility still declined
member calls and the VM treated named call-target preparation as a
non-executable boundary. That left the bytecode target metadata present but not
usable by the production route.

Named member calls add one observable requirement beyond identifier calls: the
callee must be loaded from the receiver while preserving that receiver as the
call `this` value. Widening the selector without that stack contract would make
`box.read(value)` execute but risk calling `read` with the wrong receiver.

## Decision

Allow only direct named member calls whose receiver chain is
activation-resolved and whose arguments are simple literal or slot operands to
execute through production unified bytecode.

The VM owns `PrepareNamedCallTarget` for this accepted shape:

1. keep the receiver on the stack;
2. read the named property value from that receiver using owned VM property-read
   helpers and the active `EvaluationContext`;
3. push the loaded callee after the receiver so `CallInvocationBoundary`
   consumes the existing `[receiver, callee, args...]` stack contract; and
4. invoke through the existing callable helpers without calling back into
   `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation.

Production eligibility remains narrower than compiler coverage. Computed member
calls, direct eval, spread calls, construct/super calls, optional calls,
private/super targets, arguments-object dependencies, dynamic lookup, and
receiver shapes that are not activation-resolved remain pre-VM declines until a
separate selector/compiler/VM/proof slice owns them.

## Consequences

- `PrepareNamedCallTarget` is no longer categorically non-executable, but only
  the direct named member-call shape admitted by issue #2530 may route through
  production.
- Receiver binding is part of the production bytecode contract. Future call
  slices must prove `this` behavior through public invocation tests, not only
  selector eligibility or final return values.
- The next call-family lane is computed member calls. Direct eval, spread,
  construct/super, optional calls, private names, and dynamic lookup remain
  separate deferred lanes.

## Evidence

- PR #2534 merged as commit
  `c62fef00a6d0c4dfb91edc7253757f82187014fe`.
- Build-stage delivery commit was `82b2f326` on
  `agent-go/task-gh2530` before the squash merge.
- Focused proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_NamedMemberCallExpressionPlan_AcceptsExecutableInvocationBoundary|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.NamedMemberCall_UsesUnifiedBytecodeProductionFastPathAndPreservesReceiver|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.ComputedMemberCall_DeclinesUnifiedBytecodeAndFallsBack"`
  with 3 tests.
- Production eligibility/invocation proof pack passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`
  with 195 tests.

## Related

- Issue #2530
- PR #2534
- ADR 0250:
  `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0261:
  `docs/adrs/0261-keep-unified-bytecode-call-invocation-boundary-plan-sliced-and-deferred.md`
- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
