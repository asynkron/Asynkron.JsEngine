# ADR 0340: Admit sync switch breakable wrappers through compiler-owned targets

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-1f1b588fd3`
and delivery PR #3252 worked one A51a compiler-decline leaf. Switch lowering
emits a `BreakableKind.HandlesCompletionInternally` wrapper, and the unified
bytecode compiler still rejected every breakable wrapper except
`BreakableKind.ResetsCompletionValue` with the template:

`Unsupported breakable construct: only loop-style breakable wrappers are eligible for unified bytecode compilation.`

That rejection was now too broad for ordinary sync production routing. The
compiler already owns numeric break targets for accepted loop-control shapes, so
a switch body with ordinary sync control flow can compile and execute through
the same patched-jump path. Keeping the blanket construct-kind check would have
left source syntax (`switch`) blocked after the lowerer had already reduced the
route-relevant control transfer to compiler-owned numeric targets.

The same fact does not make switch bodies broadly resumable-safe. Resumable
switch lowering still carries unsupported instruction/environment boundaries;
PR #3252 pinned that nearby shape as a pre-VM decline through
`UnifiedBytecodeProductionEligibility.EvaluateResumable`.

## Decision

Remove the compiler-only rejection based on
`BreakableKind.HandlesCompletionInternally` for ordinary sync production
unified bytecode. Treat switch-style breakable wrappers as eligible when the
existing selector and compiler checks can prove the lowered plan shape, numeric
targets, and opcode subset.

Keep the boundary route-specific:

- Do not introduce a source-syntax switch exception or a second CFG recognizer.
- Do not treat switch-wrapper admission as proof that every
  `HandlesCompletionInternally` construct is safe in every route.
- Keep resumable switch bodies declined before VM execution until the resumable
  instruction and environment model owns the needed switch-lowered shape.
- When a compiler diagnostic template is removed, update the expansion-contract
  reason inventory and the owning A51 leaf in the same slice.

## Consequences

- Sync switch bodies can use the production unified-bytecode fast path when
  their lowered control flow is otherwise in the admitted subset.
- A51a now represents remaining entrypoint, invalid-target, loop-topology, and
  loop-control diagnostics instead of the stale switch-wrapper compiler reason.
- The compiler-decline inventory remains source-guarded by the expansion
  contract instead of preserving obsolete diagnostic strings as active work.
- Future switch or breakable-wrapper widening must prove the actual route
  boundary: public sync route hits for admitted shapes and adjacent no-route
  proof for resumable or otherwise unowned shapes.

## Evidence

- Delivery PR #3252 merged as commit `a4844acd3`.
- The delivery removed the stale compiler diagnostic from
  `UnifiedBytecodeCompiler.TryValidateBreakableEnter`.
- The delivery updated:
  - `docs/unified-bytecode-expansion-contract.md`
  - `docs/plans/bytecode-burndown-checklist.md`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- Build-stage verification recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.SupportedLoopControlShapes_UseUnifiedBytecodeProductionFastPath|FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.EvaluateResumable_SwitchBody_RemainsPreVmDeclined|FullyQualifiedName~ExpressionProgramCoverageMapTests.UnifiedBytecodeCompiler_DeclineReasonTemplatesMatchExpansionContract"`
  - `rtk git diff --check`

## Related

- `docs/adrs/0322-keep-unified-bytecode-compiler-decline-inventory-source-guarded.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
