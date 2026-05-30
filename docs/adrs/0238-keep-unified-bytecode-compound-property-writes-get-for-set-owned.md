# ADR 0238: Keep unified bytecode compound property writes get-for-set owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779887420937175000-batch-1-boundary-and-baseline-gate-batch-4-f0057ffdc4`
and PR #2426 widened production unified-bytecode routing from simple
property writes and property updates to direct named and computed compound
property assignments.

The existing expression-program lowering already represented these JavaScript
forms with observable reference semantics:

- named compound writes load the base, duplicate it, get the named property,
  evaluate the RHS, apply the binary operator, and set the same named property;
- computed compound writes load the base and key, coerce the base, resolve the
  property key, duplicate the target/key pair, get the computed property,
  evaluate the RHS, apply the binary operator, and set the computed property.

The production unified-bytecode VM did not have generic stack duplication,
shuffle, or callback opcodes. Adding those generically would have admitted more
expression-program shapes than the selector, compiler, VM, and proof pack owned.
At the same time, reusing ordinary property reads for the compound get would
consume the receiver or resolved key that the existing setter opcodes still need.

## Decision

Keep production unified-bytecode compound property writes owned by dedicated
get-for-set opcodes and exact selector/compiler shapes.

1. Admit only the direct activation-resolved named shape
   `LoadBase, DuplicateTop, GetNamedProperty, SimpleRhs, Binary, SetNamedProperty`.
2. Admit only the direct activation-resolved computed shape
   `LoadBase, SimpleKey, RequireObjectCoercible(1), ResolvePropertyKey,
   DuplicateTopTwo, GetComputedProperty, SimpleRhs, Binary, SetComputedProperty`.
3. Execute the compound get with dedicated VM opcodes:
   `GetNamedPropertyForCompoundSet` preserves the receiver for
   `SetNamedProperty`, and `GetComputedPropertyForCompoundSet` preserves both
   the receiver and the already-resolved key for `SetComputedProperty`.
4. Reuse the existing VM property set and binary-operator helpers so strict
   failed writes, sloppy failed writes, receiver identity, and coercion behavior
   stay aligned with the earlier property-write boundary.
5. Keep logical writes, complex member chains, computed expression writes,
   destructuring, optional chains, `super`, private fields, `delete`, calls, and
   dynamic lookup as pre-VM declines until a later slice owns their full
   selector, compiler, VM, and route-proof behavior.
6. Do not introduce generic duplicate/swap stack opcodes, an expression-program
   callback, or an AST/IR fallback to make compound writes execute in
   production unified bytecode.

## Consequences

- Compound property writes have a production route without broadening the
  unified VM into a generic expression stack interpreter.
- Computed compound writes reuse the key after `ResolvePropertyKey`, so key
  coercion remains once per write instead of being repeated for the get and set.
- The VM still owns every accepted opcode; accepted programs do not fall back to
  `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation.
- Future widening to logical assignment, nested member chains, richer computed
  keys, private fields, optional chains, or `super` needs a separate bounded
  proof slice rather than treating this decision as broad property-write
  permission.
- Prefix/postfix property update route proof remains part of the same boundary
  evidence, but it continues to use the existing `UpdateNamedProperty` and
  `UpdateComputedProperty` opcodes rather than the compound get-for-set opcodes.

## Evidence

- Delivery PR #2426 merged as commit
  `728516a3 Expand unified bytecode compound property writes (#2426)`.
- Build-stage delivery commit
  `85f4fb05 Expand unified bytecode compound property writes` added
  `GetNamedPropertyForCompoundSet` and `GetComputedPropertyForCompoundSet`.
- Build-stage baseline signal on `origin/main` before the delivery change:
  compound get-for-set opcodes in `UnifiedBytecodeProgram.cs` = 0.
- Build-stage final signal on the delivery branch before squash merge:
  compound get-for-set opcodes in `UnifiedBytecodeProgram.cs` = 2.
- Focused build-stage verification passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 118 tests passing; `rtk git diff --check` was clean.
- Review-stage verification reran the focused test pack with 118 tests passing,
  ran `rtk dotnet build` with 11 projects, 0 errors, and 0 warnings, and found
  no review issues.
- Coverage expansion PR #2758 (issue
  `planitem-planmanual1780157100924814000-baseline-batch-4-compound-property-writes-1c79427542`)
  added 29 tests covering `this`-base compound write eligibility and invocation
  and explicit per-operator coverage across all 12 production binary operators
  (`+`, `-`, `*`, `/`, `%`, `**`, `&`, `|`, `^`, `<<`, `>>`, `>>>`). Negative
  coverage for logical-assignment operators (`&&=`, `||=`, `??=`) and multi-hop
  chains confirmed the decline boundaries. All 7 acceptance criteria passed at
  review. The eligibility path treats `LoadThis` identically to
  `LoadActivationObject` — both are activation-resolved bases accepted by
  `TryIsFirstBoundaryNamedCompoundPropertyWriteCandidate`.

## Related

- `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- `docs/adrs/0218-keep-unified-bytecode-property-read-production-boundary-selector-owned.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0224-keep-unified-bytecode-shape-probes-side-effect-free-before-emission.md`
- `docs/adrs/0231-keep-unified-bytecode-property-write-private-names-guarded.md`
- `docs/adrs/0234-keep-unified-bytecode-property-writes-strict-and-directive-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- `docs/performance/propertyaccess-unified-bytecode-production-profile-evidence.md`
