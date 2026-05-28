# ADR 0251: Keep unified bytecode iterator and destructuring drivers model-first

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-447731fb5a`
and PR #2486 handled the first explicit production boundary for two stateful
driver families in the shared unified-bytecode expansion plan:

- `ForInInitInstruction` and `ForInMoveNextInstruction`
- `ArrayDestructuringInitInstruction`, `ArrayDestructuringElementInstruction`,
  `ArrayDestructuringRestInstruction`, and
  `ArrayDestructuringCloseInstruction`

The investigation found no current unified VM model for the driver-state
lifecycle behind these families. For-in iteration owns enumeration state across
init/move-next. Array destructuring owns iterator state, element/rest stepping,
close behavior, and abrupt/suspending completion interactions. Adding isolated
opcodes for one instruction at a time would make the unified bytecode surface
look wider than the runtime model actually owns.

The delivery therefore kept the lane selector-owned and decline-first. It added
`ForInDriverStateDependency`, classified for-in driver instructions under that
decline code, classified array-destructuring driver instructions under
`DestructuringDependency`, and updated the expansion contract with an explicit
iterator/destructuring model boundary. It did not widen the opcode, compiler,
or VM execution surfaces.

## Decision

Keep for-in and array-destructuring production unified-bytecode support
model-first.

- Stateful driver families must decline before unified bytecode compilation or
  VM execution until a slice owns the full state model.
- `ForInInitInstruction` and `ForInMoveNextInstruction` decline as
  `ForInDriverStateDependency`.
- Array-destructuring driver instructions decline as `DestructuringDependency`.
- Future support must add selector, compiler, VM, state lifecycle, close/abrupt
  behavior, positive route proof, adjacent no-route proof, and expansion
  contract updates together.
- Do not add partial per-instruction opcodes, VM callbacks, or
  `ExpressionProgram` / `ExecutionPlanRunner` fallback to make one driver step
  executable before the whole driver contract is owned.

## Consequences

- Production eligibility gives stable diagnostics for unsupported for-in and
  array-destructuring plans instead of falling through to a generic unsupported
  shape.
- The unified VM remains fallback-free and does not imply iterator/destructuring
  support that it cannot execute end to end.
- Future parallel lanes can see the boundary in
  `docs/unified-bytecode-expansion-contract.md` and avoid adding opportunistic
  driver opcodes without the full semantics proof.
- The next executable slice for these families must be larger than a single
  opcode addition because the runtime state object and cleanup semantics are
  the feature boundary.

## Evidence

- PR #2486 merged commit `36fe4f7da9aea4f343428cd5561a256b4d89bc19`.
- Focused build-stage proof passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests|FullyQualifiedName~ExpressionProgramCoverageMapTests"`
  with 99 tests passing.
- Review-stage summary recorded no blocking findings and confirmed compiler,
  VM, and opcode surfaces stayed unchanged.

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
