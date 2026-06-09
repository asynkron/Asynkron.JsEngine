# ADR 0384: Close E5b runner entry point tombstones batch

## Status

Accepted.

## Context

Batch issue `planitem-planitem-gh3495-shared-context-e5b-runner-entry-point-tombstones-after-e-062ea212cc` encompassed four child issues that verified and documented E5b runner entry point handling after prior runner-retirement slices had established allowlist classification:

- **be58fb9ddb**: Verify E5-ir-runner-type-still-present proof
- **e3f725d87e**: Verify E5-ir-runner-script-entry-still-present proof
- **bd02e19f7d**: Verify E5-ir-runner-async-step-entry-still-present proof
- **62e23a32d7**: Verify E5-ir-runner-sync-entry-still-present retirement proof

All child issues have completed. Investigation confirmed source patterns match expected proof state:

- `new ExecutionPlanRunner(` appears 5 times in allowed paths (TypedAstEvaluator.ExecutionPlanRunner.Core.cs, TypedAstEvaluator.AsyncFunctionInvoker.cs, TypedAstEvaluator.IrSyncGeneratorInvoker.cs)
- `ExecutionPlanRunner.RunScript(` appears 2 times in allowed paths (Legacy/StatementNodeExtensions.cs)
- `.RunSync(` is absent across all Ast sources (retired source-absence tombstone)
- `.ExecuteAsyncStep(` is present in TypedAstEvaluator.AsyncFunctionInvoker.cs (classified to async-function declined-body runner residue)

All four manifest proofs are in verified states:
- `E5-ir-runner-type-still-present`: open allowlist
- `E5-ir-runner-script-entry-still-present`: open allowlist
- `E5-ir-runner-sync-entry-still-present`: retired-fallback (source-absence)
- `E5-ir-runner-async-step-entry-still-present`: open source-presence

## Decision

E5b batch closure is complete. All four child batch items have reached the `done` stage with all expected proof patterns confirmed.

## Consequences

The E5b checklist item remains open as a finite retirement anchor until E5c and E5d child owners complete their own runner-fallback and entry-point retirement work. The allowlist classification holds all four runner construction and entry call sites at visible boundaries for E5c/E5d decline ownership tracking.
