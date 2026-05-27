# ADR 0221: Keep unified bytecode property reads VM-owned and observable

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-2-uni-990bcd3283`
and PR #2311 made the ADR 0218 production property-read boundary executable.

ADR 0218 deliberately stopped at selector ownership. It recognized only exact
direct named reads and exact direct computed reads, then declined them as
`PropertyReadCandidateRequiresVmSupport` until the unified compiler and VM
owned the runtime semantics. The executable follow-up had to admit those
candidate shapes without widening into optional chains, calls, writes, updates,
delete, `super`, `this`, dynamic lookup, object literal/spread, or broader
computed-key expressions.

Property reads are observable. Named reads must preserve nullish-base errors,
primitive boxing, accessors, prototypes, proxies, and abrupt completions.
Computed reads additionally require the exact ordinary-read sequence:
evaluate the base and key, perform `RequireObjectCoercible(Depth: 1)` on the
base, run `ToPropertyKey` once, and then perform the property lookup. Hiding
any part of that through `ExpressionProgram`, `ExecutionPlanRunner`, or AST
callbacks would make the unified VM boundary mixed execution again.

## Decision

Keep production unified-bytecode property reads VM-owned, fallback-free, and
observable-semantics-preserving.

- Store unified named property keys in `UnifiedBytecodeProgram.StringConstants`
  and execute them with an owned `GetNamedProperty` opcode.
- Lower direct computed reads only when they match ADR 0218's exact sequence,
  then emit owned `RequireObjectCoercible`, `ResolvePropertyKey`, and
  `GetComputedProperty` opcodes in that order.
- Use existing JavaScript runtime helpers from the VM handlers:
  `JsOps.GetRequiredPropertyName(...)` for `ToPropertyKey`, and
  `JsOps.TryGetPropertyValue(...)` /
  `JsOps.TryGetPropertyValueJsValue(...)` for lookup.
- Propagate `EvaluationContext` abrupt completions after key conversion and
  property lookup instead of converting errors into ordinary `undefined`.
- Keep production eligibility and the compiler aligned: accepted property-read
  candidates must compile and execute through these owned opcodes; adjacent
  or out-of-boundary property forms must continue to decline before VM
  execution.
- Do not add an `ExpressionProgram`, `ExecutionPlanRunner`, or AST callback
  bridge for accepted property-read programs.

## Consequences

- Direct named and computed property reads can now use the
  `unified-bytecode-production-fast-path` when the activation and expression
  shapes match ADR 0218.
- The unified VM owns the stack shape and operand decoding for property reads,
  including the base/key stack transition for computed reads.
- Future property-read widening must move selector acceptance, compiler
  emission, VM helper semantics, route-priority proof, and nearby negative
  no-route proof in the same slice.
- Future support for optional chaining, member calls, writes/updates, `super`,
  dynamic lookup, or richer computed keys must add their own owned opcodes or
  decline rules. It must not reuse this ADR as permission for mixed execution.
- This ADR is caused by issue
  `planitem-planmanual1779860498694736000-batch-1-property-read-boundary-batch-2-uni-990bcd3283`
  / PR #2311.

## Evidence

- Delivery commit `2af3e3ed Add unified property read bytecode` added the
  compiler, VM, eligibility, and test changes.
- Merged commit `4dd510e6 Add unified property read bytecode (#2311)` is on
  `origin/main` in this learn worktree.
- The build-stage focused proof passed 111 tests:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodePrototypeTests|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`.
- `rtk git diff --check` was clean in the delivery update.
- The delivery seam scan found no `EvaluateExpression(` or
  `ProfileEvaluateExpression(` calls in
  `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.

## Related

- ADR 0119: `docs/adrs/0119-keep-computed-member-nullish-read-order-spec-ordered.md`
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0210: `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- ADR 0218: `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodePrototypeTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
