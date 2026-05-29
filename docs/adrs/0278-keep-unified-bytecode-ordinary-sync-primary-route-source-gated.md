# ADR 0278: Keep unified bytecode ordinary sync primary route source-gated

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-8f4d6fd0f4`
and PR #2623 closed the Batch 6 delivery for making production unified bytecode
the primary ordinary-sync attempt after the receiver-aware call lanes had
landed.

The runtime code already had the intended route order: dedicated
simple-return binary and binary-chain fast paths stayed first, accepted
production unified-bytecode programs were attempted before the broader
`SyncIrCallTrampoline`, and the generic `ExecutionPlanRunner` fallback stayed
last. The useful delivery work was therefore not a production-code rewrite. It
was making the boundary explicit and reviewable with a source gate plus synced
coverage documentation.

The stale-risk was historical. ADR 0204 and ADR 0208 remain useful route
history from earlier, narrower production slices, but ADR 0210 and later
unified-bytecode lanes had already moved the production unified route ahead of
the broad trampoline for accepted shapes. Without a focused guard, future edits
could silently restore an older priority story or let route logs prove only one
selected shape while the ordinary sync route drifted around it.

## Decision

Keep the ordinary sync production unified-bytecode route as a source-gated
priority boundary.

- Dedicated simple-return binary and binary-chain fast paths stay ahead of the
  broader production unified-bytecode route.
- Accepted ordinary sync production unified-bytecode programs are attempted
  before `SyncIrCallTrampoline` and before generic `ExecutionPlanRunner`
  interpretation.
- Route-order proof should inspect the `TryInvokeIrFast<TArgs>(...)` source
  body directly, because public route logs can prove selected behavior without
  proving the relative order of every fallback branch.
- Accepted production unified-bytecode execution stays fallback-free. The
  accepted path and VM must not delegate to `ExpressionProgram`,
  `ExecutionPlanRunner`, or AST evaluation.
- Coverage wording must keep the estimate narrow: current selector coverage is
  that accepted ordinary sync production programs attempt the VM before generic
  IR fallback. It is not a full ECMAScript function-surface claim.
- Remaining unsupported buckets stay ranked as pre-VM declines in
  `docs/unified-bytecode-expansion-contract.md`.

## Consequences

- Route priority is now a maintained source property, not only an incidental
  observation from one invocation log.
- Future unified-bytecode widening can keep the broad primary route stable
  while still protecting older specialized routes that have stronger evidence.
- Older ADRs that say the trampoline precedes unified bytecode should be read
  as historical unless the newer ADR 0210, ADR 0258, and this ADR are
  explicitly superseded.
- A future priority change must be an explicit decision with route-order proof,
  positive route logs for the selected accepted shape, and negative proof that
  protected specialized routes still win where intended.

## Evidence

- PR #2623 merged as commit
  `6b1f01d5974e9637b941f8014ad3a6b914ef1c7d`.
- Build-stage update recorded delivery commit
  `b2720f52 Guard unified bytecode primary sync route`.
- Focused proof pack passed with 315 tests and 0 warnings.
- Source-gate-only rerun passed for
  `SourceGate_OrdinarySyncRouteAttemptsProductionUnifiedBytecodeBeforeGenericIr`.
- AST-eval seam scan found no runner matches:
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`.
- `rtk ./tools/profile forloop --memory` passed with total allocated 6.82 MB.
- `rtk git diff --check` passed.

## Related

- ADR 0204:
  `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0210:
  `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- ADR 0246:
  `docs/adrs/0246-keep-unified-bytecode-expansion-contract-source-of-truth-and-drift-guarded.md`
- ADR 0258:
  `docs/adrs/0258-keep-unified-bytecode-completed-lanes-integrated-at-production-boundary.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
