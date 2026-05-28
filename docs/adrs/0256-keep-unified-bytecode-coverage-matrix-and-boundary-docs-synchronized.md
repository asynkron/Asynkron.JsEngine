# ADR 0256: Keep unified bytecode coverage matrix and boundary docs synchronized

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-651d15496c`
requested a durable documentation closeout for the current shared
unified-bytecode production boundary.

Recent Batch 5 lanes widened or integrated production coverage across property
families, completion behavior, loop-control targets, and block lexical scopes.
Those lanes are accepted under existing ADRs, but later parallel agents still
need one stable contract that keeps four surfaces synchronized in the same
slice:

- `docs/unified-bytecode-expansion-contract.md`
- `docs/roadmap.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- this ADR record

Without this synchronization step, reviewers can receive overclaimed runtime
support, planned-lane language mixed into current-state text, or stale
unsupported-bucket guidance that conflicts with
`UnifiedBytecodeProductionEligibility` decline taxonomy.

## Decision

Keep unified-bytecode coverage and boundary documentation synchronized as one
build slice whenever shared support text changes.

- The expansion contract remains the canonical current-state matrix for opcode
  inventory, decline taxonomy, no-mixed-execution boundary, and next
  unsupported buckets.
- Roadmap current-state and near-term widening text must stay aligned with the
  same accepted boundary and must not imply executable support for planned lanes.
- Agent rules must instruct later workers to update contract, roadmap, and ADR
  surfaces together in the same delivery slice when opcode inventory, decline
  taxonomy, boundary wording, or unsupported buckets change.
- The next unsupported buckets remain explicit and decline-first until dedicated
  ownership slices land: executable call invocation, iterator/destructuring
  driver-state execution, label-dependent control flow, and dynamic lookup
  families.
- Runtime semantics stay unchanged by this ADR. Source files are used as
  enum-backed truth for documentation fidelity only.

## Consequences

- Build/review agents have a stable, reviewer-scannable source for current
  unified-bytecode boundary claims and unsupported-bucket prioritization.
- Parallel widening slices can be rejected early if they update code or proof
  without synchronized contract/roadmap/rule updates.
- Drift risk shifts from ad-hoc prose to a repeatable synchronization routine,
  with existing tests continuing to enforce required headings plus current enum
  names.

## Evidence

- Focused drift guard command for the contract headings and current enum names:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums"`.
- Updated contract section `## Next Unsupported Buckets (current boundary)` and
  aligned roadmap/rule wording in the same delivery slice.

## Related

- Issue
  `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-651d15496c`
- ADR 0246: `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0250: `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0251: `docs/adrs/0251-keep-unified-bytecode-iterator-and-destructuring-drivers-model-first.md`
- ADR 0252: `docs/adrs/0252-keep-unified-bytecode-completion-lane-vm-owned.md`
- ADR 0253: `docs/adrs/0253-keep-unified-bytecode-loop-control-targets-compiler-owned.md`
- ADR 0255: `docs/adrs/0255-keep-unified-bytecode-block-lexical-scopes-program-slot-owned.md`
- ADR 0258: `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/roadmap.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProgram.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/ExpressionProgramCoverageMapTests.cs`
