# ADR 0210: Keep unified bytecode control-flow production routing operator- and shape-owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-4fb4d210a6`
and PR #2243 widened unified bytecode production routing after the earlier
direct branch-only and Binary-closed slices from ADR 0205 and ADR 0208.

The issue asked for proven branch joins, joined local updates, comparison
conditions, and the canonical condition-first loop shape to use the
all-or-nothing unified VM route. The main risk was that `Jump`, `JumpIfFalse`,
and `Binary` were previously prototype-only for production. Letting those
opcodes through by name alone would have bypassed the observable JavaScript
semantics that the statement IR, expression bytecode, and existing runtime
operators already own.

During delivery, the VM's old Binary implementation was not sufficient for
production because it used direct numeric operations. The production route had
to pass the current `EvaluationContext` into the unified VM and reuse the
existing `JsValue` operator helpers (`TypedAstEvaluator.*Value` and `JsOps`
comparators) before `Binary` could become eligible. The delivery also exposed a
route-priority friction point: accepted branch and loop shapes were still being
claimed by the broad `SyncIrCallTrampoline`. The final implementation kept
specialized simple-return binary shortcuts ahead of unified bytecode, but moved
the production unified route ahead of that broad trampoline and covered the
priority boundary with invocation log tests.

## Decision

Keep production control-flow routing owned by both operator semantics and
compiler-emitted shape.

- Use the existing unified compiler as the shape boundary for branch joins,
  joined-local updates, direct branches, and the canonical condition-first
  loop back-edge. Do not add source-syntax exceptions or a second selector-side
  control-flow recognizer.
- Keep the production selector decline-first for activation and expression
  hazards: async/generator functions, captured or dynamic activation,
  arguments, `this`, `new.target`, calls/constructs, dynamic lookup, labels,
  break/continue, unsupported expression payloads, and unsupported opcodes
  must still decline before VM execution.
- Admit `Jump` and `JumpIfFalse` only as part of bytecode programs emitted by
  the existing compiler-owned accepted shapes. Nearby unsupported control flow
  remains declined by the compiler or plan inspection before the VM starts.
- Promote only the current Binary production subset: `+`, `-`, `*`, `/`, `%`,
  `==`, `<`, `<=`, `>`, and `>=`. Unsupported Binary operators still decline as
  `PrototypeOnlyBinaryOpcode` with operator-specific diagnostics.
- Execute production Binary through the existing `JsValue` runtime operator
  helpers and an `EvaluationContext`, not through direct numeric extraction or
  host-only arithmetic.
- Keep `UnifiedBytecodeVirtualMachine` fallback-free. It executes accepted
  unified instructions only; it must not call back to `ExpressionProgram`,
  `ExecutionPlanRunner`, or AST evaluators for unsupported shapes.
- Keep runtime priority explicit: direct specialized simple-return binary and
  binary-chain fast paths stay ahead of unified bytecode, while the production
  unified route intentionally runs ahead of the broader `SyncIrCallTrampoline`.
- Keep proof paired: selector acceptance, VM semantics, public invocation route
  logs for both branch outcomes and canonical loop cases, and negative
  no-route assertions for labels, break/continue, noncanonical loops, calls,
  and unsupported payloads.

## Consequences

- ADR 0208 remains useful history for the first direct branch production slice,
  but this ADR owns the current production boundary for branch joins,
  canonical loops, and the Binary subset used by their conditions and updates.
- Future production widening cannot treat prototype compiler support as enough.
  A new operator or control-flow family needs the same paired selector, VM,
  route-priority, and public invocation proof.
- Noncanonical loop/control-flow families stay visible as explicit declines
  instead of silently falling back inside the VM.
- The current route-order exception is intentional and bounded: unified
  production routing may preempt the broad trampoline, but it must not preempt
  the more specialized direct simple-return binary fast paths without a
  separate decision and regression proof.

## Evidence (Batch 5 proof run)

- `UnifiedBytecodeProductionEligibilityTests` passed in Release: 31 tests.
- `UnifiedBytecodeProductionInvocationTests` passed in Release: 17 tests.
- AST-eval seam scan command:
  `rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*`
  returned `NO_MATCHES`.
- Memory profile command `rtk ./tools/profile forloop --memory` reported
  `Total allocated 6.70 MB` then `Total allocated 6.80 MB` in repeated samples.

Interpretation: this is allocation-stability evidence for the routed production
boundary, not a performance-improvement claim.

## Evidence (Issue #2256 widening slice)

- `UnifiedBytecodeProductionEligibilityTests` passed in Release after widening
  `BinaryOperator.Equal`: 32 tests.
- `UnifiedBytecodeProductionInvocationTests` passed in Release with explicit
  `==` route-log proof and `===` no-route proof: 19 tests.

## Related

- Issue
  `planitem-planmanual1779822558747978000-batch-1-production-eligibility-boundary-ba-4fb4d210a6`
- PR #2243
- Commit `e2c84d409ea11d6a2578979ea76e42f79308e8ee`
- ADR 0192: `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0205: `docs/adrs/0205-keep-unified-bytecode-binary-production-eligibility-operator-explicit.md`
- ADR 0208: `docs/adrs/0208-keep-unified-bytecode-branch-production-routing-shape-discriminated.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
