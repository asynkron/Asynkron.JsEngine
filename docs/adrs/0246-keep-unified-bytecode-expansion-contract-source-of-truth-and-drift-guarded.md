# ADR 0246: Keep unified bytecode expansion contract source-of-truth and drift-guarded

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-25de646b9f`
and PR #2466 added `docs/unified-bytecode-expansion-contract.md` before the
next set of parallel unified-bytecode lanes starts. The delivery was
documentation plus a focused drift guard, not a runtime widening.

The pressure behind the issue was coordination: the unified compiler, VM,
production selector, statement diagnostics codec, and expression-bytecode
coverage map are all central surfaces. Parallel agents widening nearby lanes
can otherwise rediscover the same source-of-truth files, blur current support
with planned support, or create conflicts in the same handler switches.

Earlier ADRs already keep unified bytecode IR-owned, decline-first, and
fallback-free for accepted production programs. This ADR records the shared
expansion-contract policy layered on top of those runtime decisions.

## Decision

Keep `docs/unified-bytecode-expansion-contract.md` as the source-of-truth
coordination surface for parallel unified-bytecode expansion.

- The contract must name the current source-of-truth implementation surfaces
  for opcode definitions, compiler ownership, VM ownership, production
  eligibility, statement diagnostics, and expression-bytecode coverage.
- It must distinguish current support from reserved or planned ownership lanes.
  Reserved lanes are planning boundaries, not evidence of runtime support.
- The no-mixed-execution rule stays explicit: production-eligible unified
  programs must execute fully in `UnifiedBytecodeVirtualMachine` and must not
  delegate back to `ExpressionProgram`, `ExecutionPlanRunner`, or AST
  evaluators.
- Future opcode, production-decline, or proof-command changes should update the
  contract in the same slice that changes the owning implementation surface.
- The drift guard in
  `ExpressionProgramCoverageMapTests.UnifiedBytecodeExpansionContract_ListsRequiredHeadingsAndCurrentEnums`
  must continue to require the contract headings and current
  `UnifiedBytecodeOpCode` / `UnifiedBytecodeProductionDeclineCode` names.

## Consequences

- Parallel unified-bytecode work has a stable handoff document before touching
  centralized compiler, selector, and VM switches.
- Review can reject source-shaped or planned-lane claims that are not backed by
  current support entries and proof commands.
- The contract remains cheap to maintain: enum-name drift and missing required
  sections fail in the internal test suite without parsing the full Markdown
  matrix.
- Runtime widening still needs its own selector, compiler, VM, positive route
  proof, negative decline/no-route proof, AST-eval seam scan, and memory/profile
  stability evidence when the slice changes execution behavior.

## Related

- Issue
  `planitem-planmanual1779943568009120000-batch-1-shared-bytecode-surface-and-parall-25de646b9f`
- PR #2466
- `docs/unified-bytecode-expansion-contract.md`
- `tests/Asynkron.JsEngine.Tests/ExpressionProgramCoverageMapTests.cs`
- `.claude/rules/unified-bytecode-prototypes.md`
- ADR 0181: `docs/adrs/0181-keep-unified-bytecode-prototype-ir-owned-and-all-or-nothing.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
