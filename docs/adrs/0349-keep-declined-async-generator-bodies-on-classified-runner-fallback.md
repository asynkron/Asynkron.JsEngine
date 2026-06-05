# ADR 0349: Keep declined async-generator bodies on classified runner fallback

## Status

Accepted.

## Context

Issue #gh3295 repaired red `main` after PR #3291 retired the remaining
`AsyncGeneratorInvoker` runner fallback. The retirement made route-ineligible
async-generator bodies fail fast during initialization instead of settling
through the existing async-generator runner path. That broke ordinary runtime
behavior for bodies whose unified-bytecode route still declines, including
non-simple parameter initialization and captured hoisted helper shapes.

The admitted async-generator production route remains valuable: direct-yield,
delegated `yield*`, and awaited-source `yield* await ...` bodies can step
through `UnifiedBytecodeVirtualMachine.ExecuteResumable` and reuse the existing
promise settlement contract. But route admission is still narrower than
complete async-generator semantics. Some declined bodies require effects before
iterator creation or body-environment lifetime that the resumable VM does not
own yet.

## Decision

`AsyncGeneratorInvoker` keeps a classified declined-body fallback to
`ExecutionPlanRunner.ExecuteAsyncStep` until the unified-bytecode resumable route
owns the missing semantics for those bodies.

Accepted async-generator bodies still route through `UnifiedBytecodeResumeState`
and `UnifiedBytecodeVirtualMachine.ExecuteResumable`. Declined bodies must keep
stable no-route behavior, execute through the existing runner bridge, and settle
`next`, `return`, and `throw` promises through the same async-generator
settlement path.

Future widening must prove both sides of the boundary:

- public route-hit tests for newly admitted async-generator bodies;
- public no-route tests for nearby declined bodies;
- source gates that accepted route setup and VM execution do not delegate back
  to `ExecutionPlanRunner`, `ExpressionProgram`, or AST/expression evaluation
  bridges.

Do not replace declined-body fallback with fail-fast initialization unless the
issue also proves that all formerly declined async-generator bodies are now
owned by the VM or intentionally rejected by JavaScript semantics.

## Consequences

- The async-generator invoker remains a mixed routing boundary for now:
  accepted bodies use the VM, declined bodies use the classified runner bridge.
- Non-simple parameter lists and captured hoisted helpers continue to behave as
  valid async generators while their VM ownership remains incomplete.
- Tests should assert no-route settlement for declined neighbors, not
  fail-fast initialization.
- Rule 10d in `docs/rules/unified-bytecode-prototypes.md` records this
  preventive boundary for future unified-bytecode widening.

This ADR is caused by issue #gh3295 / PR #3298, which restored the classified
declined-body fallback after local `main` verification failed at commit
`98a1472ea71d42b629c61fbd477578e967b51433`.
