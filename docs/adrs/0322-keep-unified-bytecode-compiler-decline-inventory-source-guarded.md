# ADR 0322: Keep unified bytecode compiler decline inventory source-guarded

## Status

Accepted

## Context

Issue #3134 / PR #3138 decomposed the full-bytecode checklist's
`UnifiedBytecodeCompiler.TryCompile` decline umbrella. Before the delivery,
P0.2, A51, B47, and E2 treated compiler failure as one coarse
`UnsupportedPlanShape` bucket, even though the compiler already emitted many
diagnostic reason strings for unsupported shapes.

The tempting repair was to promote every compiler reason into a separate
production decline enum member. That would have made the checklist look more
granular, but it would also have confused two different contracts:

- `UnifiedBytecodeProductionDeclineCode` is the production routing family seen
  by eligibility and invocation routes.
- `UnifiedBytecodeCompiler.TryCompile` reason strings are compiler diagnostics
  emitted after eligibility has already admitted the plan-shaped candidate far
  enough to attempt compilation.

The delivery instead kept failed compilation wrapped as `UnsupportedPlanShape`
while adding owner leaves A51a-A51m and B47a in the burn-down checklist and an
exact reason-template inventory in `docs/unified-bytecode-expansion-contract.md`.
The new `ExpressionProgramCoverageMapTests` source gate compares non-empty
`reason = ...` templates in `UnifiedBytecodeCompiler.cs` against the contract
section so compiler diagnostic drift cannot silently hide under A51/B47 again.

## Decision

Keep the compiler-decline inventory source-guarded instead of turning compiler
diagnostic strings into production decline enum members.

- Leave failed `UnifiedBytecodeCompiler.TryCompile` attempts wrapped as
  `UnsupportedPlanShape` until a later slice changes production routing
  semantics explicitly.
- Track concrete compiler work through checklist owner leaves A51a-A51m and
  B47a, including sync/resumable applicability and the current fallback owner.
- Keep the exact compiler reason templates in
  `docs/unified-bytecode-expansion-contract.md`.
- When adding, deleting, or editing a non-empty compiler `reason = ...`
  assignment, update the expansion contract and the relevant checklist owner
  leaf in the same delivery.
- Keep dynamic residue such as direct eval and Function-constructor bodies out
  of the compiler-decline leaf count unless the work is actually a
  compiler-owned non-dynamic route gap.

## Consequences

- Production decline families stay stable and route-facing while the burn-down
  checklist still exposes real compiler-owned work.
- Future compiler widening cannot add new diagnostic strings without updating
  the human roadmap artifact that owns the work.
- Checklist counters must be maintained with the decomposition. PR #3138 needed
  a follow-up counter repair after review found stale Phase B and footer counts.
- A future decision to add more granular production decline codes remains
  possible, but it must be justified as a routing/API contract change rather
  than a documentation-only inventory repair.

## Evidence

- Delivery PR #3138 merged as commit `aec037d7e`.
- The delivery added:
  - `docs/unified-bytecode-expansion-contract.md` sections
    `Compiler Decline Owner Leaves (current)` and
    `Compiler Decline Reason Templates (current)`.
  - A51a-A51m and B47a in
    `docs/plans/bytecode-burndown-checklist.md`.
  - `ExpressionProgramCoverageMapTests.UnifiedBytecodeCompiler_DeclineReasonTemplatesMatchExpansionContract`.
- Review feedback found stale checklist counters after the initial delivery
  slice; commit `94de69af9` repaired the Phase B and footer counts before merge.
- Build-stage local verification recorded:
  - `rtk rg -n "Phase B count of 47|Status: 27 / ~119|~119|P0\.2/E2|Status: 31 / ~131" docs/plans/bytecode-burndown-checklist.md`
  - `rtk git diff --check`
  - `rtk git diff --check origin/main...HEAD`

## Related

- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
- `tests/Asynkron.JsEngine.Tests/ExpressionProgramCoverageMapTests.cs`
- `docs/rules/unified-bytecode-prototypes.md`
