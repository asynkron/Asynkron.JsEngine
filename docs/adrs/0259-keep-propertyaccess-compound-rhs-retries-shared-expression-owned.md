# ADR 0259: Keep propertyaccess compound RHS retries shared-expression-owned

## Status

Accepted

## Context

Issue `autrun-diu68o64336o-198e1cf9f1` / PR #2506 selected the
`propertyaccess` recurring optimizer workload after the full benchmark table
still showed a current Asynkron-vs-Jint loss:

```text
propertyaccess  asynkron_ms=906  jint_ms=561  Jint 1.61x faster
```

The focused pre-edit row was different:

```text
propertyaccess  asynkron_ms=987  jint_ms=1037  Tie
```

The CPU profiles consistently named the compound-assignment RHS expression
program path:

```text
ExecuteInstructionLoop
-> HandleCompoundAssignmentSlot
-> HandleCompoundAssignmentSlotSlow
-> EvaluateExpressionProgram
-> GetProgramNamedPropertyValue
-> JsOps.TryGetPropertyValue
-> JsObject.TryGetProperty
-> JsObject.TryGetSimplePropertyWithReceiver
```

Two variants were tried and reverted:

1. a runner-local simple named-property RHS evaluator for a small expression
   program subset; and
2. a smaller slot-aware compound target read/write change that used
   `TryReadIdentifierWithSlot` / `TryWriteIdentifierWithSlot` before generic
   identifier cache fallback.

The first variant preserved getter order in focused tests, but the new helper
became its own owner surface and did not improve meaningfully over the 906 ms
full-table baseline row. The second variant produced one good selected-profile
row, but repeated timings were noisy:

```text
propertyaccess  814 ms
propertyaccess  946 ms
propertyaccess  909 ms
```

The best row was about 10.2% faster than the 906 ms full-table baseline, but
the median final Asynkron time was 909 ms. That did not clear the recurring
optimizer gate beyond noise, so all runtime and test edits were reverted. The
delivery retained only
`docs/performance/failed-propertyaccess-compound-rhs-fast-path.md`. A
review-back fix in commit `43d55a17` corrected the retained evidence label from
`focused row = 906 ms` to `full-table baseline row = 906 ms`; the numbers did
not change.

## Decision

Keep future `propertyaccess` compound-assignment RHS retries shared-expression
owned and timing-gated.

Do not add or retain another runner-local mini interpreter for the compound RHS
just because the profile names
`HandleCompoundAssignmentSlotSlow -> EvaluateExpressionProgram ->
GetProgramNamedPropertyValue`. A parallel evaluator that still decodes
`ExpressionProgram` operations and performs the same identifier/property lookup
work is too likely to move overhead around instead of removing it.

Future work on this owner should start from one of these owned boundaries:

1. lowering or emit-time normalization that gives the profiled loop real
   flat-slot IDs while preserving ECMAScript read/evaluate/compute/write order;
2. a compact encoded expression-program execution path owned by the shared
   `ExpressionProgram` runtime and its existing diagnostics/printers; or
3. a unified-bytecode selector/compiler/VM widening that owns the whole
   accepted compound shape without bridging back to `ExpressionProgram`,
   `ExecutionPlanRunner`, or AST evaluation.

Any retained change must prove the current owner with before/after CPU
profiles, label full-table baseline rows separately from focused selected rows,
and clear the issue's repeated selected-profile improvement threshold beyond
timing noise. A single best row is not enough when the median remains at the
baseline.

## Consequences

- The failed performance note is useful negative evidence, not a retained
  optimization.
- `EvaluateExpressionProgram` remains the shared compound-RHS execution owner
  until a future slice removes operation decoding, identifier lookup, or
  property-read overhead at an owned boundary.
- Future agents should not retry the same simple named-property RHS evaluator
  or slot-aware target read/write micro-slice without fresh profile evidence and
  repeated A/B timing that clears the gate.
- Performance notes must preserve the provenance of baseline rows. A
  full-table baseline row and a focused selected-profile row can both be valid
  evidence, but they are not interchangeable labels.
- The older property-read and compound-write ADRs remain in force: property
  reads must preserve receiver/observable semantics, and accepted unified
  bytecode property shapes must stay VM-owned and fallback-free.

## Related

- `docs/performance/failed-propertyaccess-compound-rhs-fast-path.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `docs/adrs/0188-keep-named-property-read-fast-paths-storage-owned-and-receiver-preserving.md`
- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0222-keep-unified-bytecode-two-hop-named-property-read-boundary-owned.md`
- `docs/adrs/0238-keep-unified-bytecode-compound-property-writes-get-for-set-owned.md`
- `docs/adrs/0097-keep-expression-program-operation-storage-owner-encoded.md`
