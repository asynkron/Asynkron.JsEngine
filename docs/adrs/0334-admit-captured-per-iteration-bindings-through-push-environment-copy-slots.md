# ADR 0334: Admit captured per-iteration bindings through PushEnvironment copy slots

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-c99c23d77d`
and PR #3231 widened ordinary sync production unified bytecode for A44:
captured per-iteration `let` / `const` loop bindings.

Before this delivery, non-captured per-iteration bindings could route because
the loop binding lived entirely in flat slots. Captured bindings were declined.
The closure needs a fresh binding per iteration, but the earlier unified
`PushEnvironment` model only reset lexical flat slots to
`JsValue.Uninitialized`. Relaxing the gate without a copy model produced a
spurious TDZ failure: the closure-visible per-iteration environment was
created, but the previous iteration value was not copied into the new binding.

The important ownership split is between ordinary block lexical scopes, which
start in TDZ, and per-iteration loop scopes, which must preserve selected
binding values across `CreatePerIterationEnvironment`-style rebinding.

## Decision

Admit captured per-iteration `let` / `const` loop bindings by making the
per-iteration copy list program metadata consumed by `PushEnvironment`.

- `SlotAssignmentRewriter` eagerly creates flat mappings for admitted
  `PushEnvironment` lexical slots, while sharing the active loop-head flat slot
  for bindings listed in `PerIterationBindings`.
- `UnifiedBytecodeCompiler` remaps `PerIterationBindings` into
  `UnifiedBytecodeScopeDescriptor.PerIterationCopySlotIndices`.
- `UnifiedBytecodeVirtualMachine` snapshots those copy-listed slots before
  rebinding the scope, skips the ordinary TDZ wipe for copied slots, then writes
  the copied value into both the flat slot and the freshly-created scope
  environment.
- Non-copy lexical slots keep ordinary TDZ initialization, including const
  marking.
- The admission is ordinary sync production only. Resumable
  `PushEnvironmentInstruction` remains a separate allowlist gap until the
  resumable route owns scope environment lifetime across suspension.
- A43 Annex B block-function declarations remain separate. Their descriptor
  backed block function binding and strict/sloppy scoping constraints are not
  solved merely because A44 per-iteration loop bindings now have copy metadata.

## Consequences

- Future `PushEnvironment` widening must distinguish TDZ initialization from
  per-iteration value carry-forward. Treating all lexical slots as TDZ on entry
  regresses captured loop closures.
- Compiler, program descriptor, and VM handling must change together whenever a
  new scope-entry semantic needs data beyond lexical/const slot indices.
- Route tests for this family need both value semantics and production-route
  assertions, because falling back to the IR runner can hide a broken VM copy
  path.
- The old "captured per-iteration bindings imply no flat mapping" rule is no
  longer current for ordinary sync production. The admitted subset is "flat
  mapped plus copy-listed", not "slotless dynamic environment".
- Remaining scope-environment work should not infer Annex B block-function or
  resumable scope safety from A44. Those routes need their own environment
  ownership proof.

## Evidence

- PR #3231 merged as squash commit
  `2fd07e5789202de75fc67ec7e75f7ff0d9f93d63`.
- Implementation changed
  `src/Asynkron.JsEngine/Execution/SlotAssignmentRewriter.cs`,
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeCompiler.cs`,
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`,
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`,
  and
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`.
- Focused proof lives in
  `tests/Asynkron.JsEngine.Tests/A44PerIterationLetDeclineTests.cs`, now route
  asserting captured `let`, dynamic-path captured `let`, and multi-captured
  `const` `for...of` cases.
- Build-stage verification recorded
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~A44PerIterationLetDeclineTests|FullyQualifiedName~ConstAssignmentTests|FullyQualifiedName~ComplexNestedScopesWithClosure|FullyQualifiedName~Generator_YieldStarThrowDoneFalseContinuesForGeneratorIteratorIr|FullyQualifiedName~Layer4_ForAwaitOf_WithVar_NotLet"`
  passing 19 tests, plus `rtk git diff --check` passing.

## Related

- ADR 0255:
  `docs/adrs/0255-keep-unified-bytecode-block-lexical-scopes-program-slot-owned.md`
- ADR 0288:
  `docs/adrs/0288-admit-tdz-head-environments-for-sync-iterator-and-for-in-drivers.md`
- `docs/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
