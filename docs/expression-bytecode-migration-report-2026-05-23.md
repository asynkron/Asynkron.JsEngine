# Statement Bytecode Migration Report (2026-05-23)

Plan lineage: `planmanual1779454308935867000` - Part 6 final decision work (`issue #planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-cfc3b74783`)

## Why this report exists
- Story 1: give maintainers a current, explicit recommendation for the next safe statement-bytecode migration step.
- Story 2: compare runtime routing, record-backed removal, and deferred-payload normalization against current evidence so follow-up scope is defensible.

## Current contract snapshot
- Runtime source of truth remains record-backed `ExecutionPlan.Instructions`.
- Compact statement storage currently serves diagnostics/estimation/parity-oriented surfaces, not execution routing.
- This report does not change runtime semantics or authorize broad runtime storage switching.

Evidence surfaces:
- `docs/adrs/0094-compact-statement-bytecode-encoding-design-from-current-ir.md`
- `src/Asynkron.JsEngine/Execution/ExecutionPlan.cs`
- `src/Asynkron.JsEngine/Execution/StatementInstructionStorageDiagnostics.cs`
- `src/Asynkron.JsEngine/Execution/StatementInstructionDiagnosticsCodec.cs`
- `tests/Asynkron.JsEngine.Tests/StatementInstructionStorageDiagnosticsTests.cs`

## Decision
Recommendation: take another deferred-payload normalization wave before broader compact runtime routing or record-backed storage removal.

## Why this is the safest next step
Current diagnostic profile still reports unsupported statement families in the hot path:
- `assignment-and-mutation`
- `declaration-and-scope`
- `branch-control`
- `suspend-and-exception-flow`

Refreshed profile signal (`forloop --statement-instruction-storage`):
- `instructions=18`
- `supported=12`, `unsupported=6`
- Unsupported histogram includes `PopEnvironment`, `IncrementSlot`, `BreakableEnter`, `Branch`, `CompoundAssignmentSlot`

This means runtime-routing/removal work would bundle unfinished normalization with storage-switching risk. A narrower normalization wave keeps semantics safer and improves readiness evidence first.

## Why other options are deferred
### Deferred option A: broader compact runtime routing now
Deferred because unsupported family counts are still non-trivial in current profile output, and diagnostics parity does not yet imply execution parity for all instruction families.

### Deferred option B: remove record-backed instruction storage now
Deferred because `ExecutionPlan.Instructions` is still the runtime contract boundary. Removing it before unsupported-family burn-down would force mixed fallback behavior or high-risk migration coupling.

## Refreshed proof evidence (build-stage)
### 1. Statement storage diagnostics tests
Command:
- `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~StatementInstructionStorageDiagnosticsTests"`

Result:
- Passed: `20` tests.

### 2. Statement storage profile output
Command:
- `rtk dotnet run --project tools/ProfileRunner/ProfileRunner.csproj -c Release -- forloop --statement-instruction-storage`

Result excerpt:
- `plans: 2`
- `instructions: 18`
- `support_shape: supported=12, unsupported=6`
- `owner_storage: encoded_bytes=192, estimated_compact_encoded_bytes=272`
- `unsupported_family_reason_histogram:`
  - `assignment-and-mutation: 2`
  - `declaration-and-scope: 2`
  - `branch-control: 1`
  - `suspend-and-exception-flow: 1`

### 3. Runner AST seam scan
Command:
- `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`

Result:
- No matches (`rg` exit `1` / expected no-match signal).

### 4. Forloop memory profile
Command:
- `rtk ./tools/profile forloop --memory`

Result excerpt:
- `Total allocated 6.98 MB`
- Build/profiler run completed successfully.

## Acceptance criteria mapping
- AC-1: New dated report added under `docs/` and linked to parent plan lineage.
- AC-2: One explicit recommendation provided (deferred-payload normalization wave).
- AC-3: Non-selected options deferred with compact-boundary and unsupported-family evidence.
- AC-4: Includes refreshed diagnostics test/profile/seam-scan/memory-profile outputs.
- AC-5: Preserves runtime contract that record-backed `ExecutionPlan.Instructions` remains source of truth for now.
- AC-6: No runtime semantics changes; evidence/reporting-only slice.

## Notes
- This issue remains evidence and migration-decision focused; it intentionally avoids runtime-routing implementation.
