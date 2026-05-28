# ADR 0252: Keep unified bytecode completion lane VM-owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-eb801dff13`
and PR #2488 widened production unified bytecode for completion and
expression-statement behavior. The accepted lane added:

- `ReturnUndefined` for explicit `return;` and implicit function completion.
- `Throw` for non-awaited `ThrowInstruction` payloads.
- production acceptance for discarded property write/update expression
  statements whose underlying property opcodes were already VM-owned.

The key boundary was completion ownership, not broad statement fallback. Empty
returns should not synthesize stack state or route through the runner. Throw
statements should not throw a host exception directly or bypass JavaScript
`try`/`catch`; the VM evaluates the owned throw payload, calls
`EvaluationContext.SetThrow(...)`, and returns through the existing invocation
bridge. Discarded expression statements still compile the supported
`ExpressionProgram` first and append `Pop`, so side effects and abrupt
completions happen before the result is discarded.

The build also hit a rebase conflict after local `origin/main` had added
explicit for-in/destructuring production declines. The accepted resolution kept
those model-first declines and removed only the now-redundant discarded
property write/update veto, because those operations were already owned by the
selector, compiler, VM, and route-proof surface.

## Decision

Keep the production unified-bytecode completion lane VM-owned and
decline-first.

- `ReturnInstruction` with no `ReturnProgram` and no `AwaitedProgram` compiles
  to `ReturnUndefined`.
- Non-awaited `ThrowInstruction` compiles its `ThrowProgram` through owned
  expression-op flattening, then emits `Throw`.
- The VM handles `Throw` by setting `EvaluationContext` throw state and
  returning through the existing caller bridge, preserving catchability.
- `EvaluateAndDiscard` support remains expression-program-shaped: compile only
  supported owned expression operations and append `Pop`. Do not skip side
  effects, swallow abrupt completions, or add an eval/fallback opcode.
- Removing a special discarded-expression decline is valid only when the
  underlying expression operations are already production-owned. Adjacent call,
  dynamic lookup, optional-chain, `super`, delete, destructuring, for-in driver,
  and unowned computed-key families must still decline before VM execution.
- Directive prologue support remains narrow and must keep lexical strictness
  threaded into the VM; strict discarded writes need positive fast-path proof.

## Consequences

- Empty and implicit returns can use production unified bytecode without stack
  hacks or runner fallback.
- Throw statements are observable through JavaScript `try`/`catch` using the
  same completion-state bridge as the existing runtime.
- Discarded side-effect statements can route only when the side-effect opcodes
  are already owned, so the lane does not become a generic expression VM.
- Future completion widening must move selector, compiler, VM semantics,
  positive route proof, nearby decline/no-route proof, expansion-contract
  inventory, AST seam scan, and memory/profile stability together.
- Conflict resolution around this area should preserve explicit model-first
  declines from sibling lanes and remove only narrow vetoes superseded by
  current owned VM semantics.

## Evidence

- PR #2488 merged commit
  `b96eb6ebabb4f362b21c8c0c75d108e84cb91a2e`.
- Build-stage commit before rebase:
  `73ebedcba3fdef8113d6270cbb17c95ddc8adf82`.
- Build-stage focused proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests|FullyQualifiedName~ExpressionProgramCoverageMapTests" -- xUnit.MaxParallelThreads=1 -timeout 20000`
  with 177 tests passing.
- Review-stage focused proof passed the same filter with 179 tests passing
  after rebasing onto local `origin/main`.
- `rtk ./tools/profile forloop --memory` passed with total allocated `6.72 MB`.
- `rtk make quality` passed in review.
- Review-stage summary recorded no blocking findings and deploy merged PR
  #2488.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0234: `docs/adrs/0234-keep-unified-bytecode-property-writes-strict-and-directive-owned.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0248: `docs/adrs/0248-keep-unified-bytecode-primitive-operators-vm-owned-and-tdz-aware.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
