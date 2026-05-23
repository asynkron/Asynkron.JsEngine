# ADR 0098: Keep statement runtime routing behind deferred-payload normalization

## Status

Accepted

## Context

Issue
`planitem-planmanual1779454308935867000-push-bytecode-from-diagnostics-toward-runt-cfc3b74783`
/ PR #1625 produced the 2026-05-23 migration decision report for compact
statement bytecode. The report compared three possible next steps:

1. route broader runtime reads through compact statement storage;
2. remove record-backed `ExecutionPlan.Instructions`; or
3. run another deferred-payload normalization wave first.

The current proof snapshot reported:

- `forloop --statement-instruction-storage`: `supported=12`, `unsupported=6`;
- unsupported-family reasons still include assignment/mutation,
  declaration/scope, branch/control, and suspend/exception flow;
- the `ExecutionPlanRunner` AST-eval seam scan stayed clean; and
- `./tools/profile forloop --memory` stayed allocation-stable at 6.98 MB.

This means the compact owner and diagnostic surfaces are useful planning
evidence, but they are not yet a complete runtime-storage contract.

## Decision

Keep broad compact statement runtime routing and record-backed instruction
storage removal behind another deferred-payload normalization wave.

The next safe migration step is to keep reducing unsupported/deferred statement
families with owner-backed payload normalization and semantic decode parity
proof before changing `ExecutionPlanRunner` or removing
`ExecutionPlan.Instructions`.

## Consequences

- Runtime execution remains record-backed until a later runtime-routing slice
  proves full owner/decode/runner parity for the targeted families.
- Diagnostic support counts and storage estimates remain migration readiness
  signals, not authority to switch execution storage.
- Future statement compact-storage work should use the unsupported-family
  histogram to choose bounded normalization slices before attempting a broad
  runtime flip.
- A future runtime-routing proposal should include current
  `--statement-instruction-storage` output, focused parity tests, the runner
  AST-eval seam scan, and `./tools/profile forloop --memory` from the same
  worktree.

## Related

- `docs/expression-bytecode-migration-report-2026-05-23.md`
- `docs/adrs/0094-compact-statement-bytecode-encoding-design-from-current-ir.md`
- `.claude/rules/statement-bytecode-packing.md`
