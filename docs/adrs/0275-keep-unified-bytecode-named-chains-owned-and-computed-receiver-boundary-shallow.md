# ADR 0275: Keep unified bytecode named chains owned and computed receiver boundary shallow

## Status

Accepted

## Context

Issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-3cea46640b`
and PR #2609 widened production unified bytecode after the receiver-aware
member-call lane had already landed.

Before this slice, production named property reads were capped at direct and
two-hop named chains, and member-call receiver preparation rejected receiver
chains deeper than the shallow direct named-member boundary. That kept the
accepted compiler and VM surface smaller than the already-owned
`GetNamedProperty`, `PrepareNamedCallTarget`, and `CallInvocationBoundary`
semantics.

The useful decision was not to add a broad expression fallback or a new opcode.
The delivery reused existing VM-owned named property reads to admit arbitrary
optional-free named chains, and it let direct named member calls prepare deeper
named receivers while preserving the deepest receiver as `this`. Computed
member-call receiver chains stayed shallow because the surrounding computed-key
and receiver-ordering proof has not been widened for deeper computed callee
neighbors.

## Decision

Production unified bytecode may admit activation-resolved, optional-free named
property chains of arbitrary depth when every hop is a non-private
`GetNamedProperty`.

For direct named member calls, `PrepareNamedCallTarget` may use the same named
receiver-chain emission so a call such as `root.child.branch.leaf.read(value)`
executes through `CallInvocationBoundary` with `root.child.branch.leaf` as the
call `this` value.

Keep direct computed member-call receiver chains on the existing shallow
boundary. A deeper named receiver followed by a computed call target, such as
`root.child.branch.leaf[key](value)`, remains a pre-VM decline until a later
slice owns the selector, compiler, VM, and proof for that combined shape.

Do not satisfy deeper named reads or deeper named member calls by calling back
into `ExpressionProgram`, `ExecutionPlanRunner`, AST evaluation, or a generic
host-call fallback.

## Consequences

- The old "exact two-hop named read" wording is historical. Current production
  support is arbitrary-depth, activation-resolved, optional-free named reads.
- Named member-call receiver binding is depth-independent for accepted named
  chains: the final resolved receiver is the call receiver.
- Computed member-call widening remains intentionally separate. Existing
  computed member-call support still owns simple computed keys and shallow
  accepted receiver chains only.
- Future proof packs should pair accepted deeper named reads and named calls
  with adjacent declines for optional chains, private names, computed call
  neighbors, super/private targets, spread/eval/construct, and dynamic lookup.

## Evidence

- PR #2609 merged as commit
  `ccbced2cf9eab98ebcd247037d63aa33ae4b7350`.
- Build-stage delivery commit was `f13ead50` on
  `agent-go/task-planitem-planmanual1779965179415360000-batch-1-receiver-aware-47e706d5af`.
- Focused proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~DeeperNamed|FullyQualifiedName~Evaluate_PropertyReadAdjacentFamilies_DeclineWithExplicitCodes|FullyQualifiedName~ComputedPropertyInNamedChain_DeclinesUnifiedBytecodeAndFallsBack"`
  with 28 tests.
- Production eligibility/invocation proof pack passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests"`
  with 238 tests.
- The AST-eval seam scan over
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*` reported
  no `EvaluateExpression(` or `ProfileEvaluateExpression(` matches.
- `rtk git diff --check` was clean in the delivery stage.

## Related

- PR #2609
- Issue
  `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-3cea46640b`
- ADR 0218:
  `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- ADR 0221:
  `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- ADR 0222:
  `docs/adrs/0222-keep-unified-bytecode-two-hop-named-property-read-boundary-owned.md`
- ADR 0250:
  `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0264:
  `docs/adrs/0264-keep-unified-bytecode-member-call-final-receiver-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
