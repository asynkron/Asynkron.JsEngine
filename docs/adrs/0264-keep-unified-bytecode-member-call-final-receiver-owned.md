# ADR 0264: Keep unified bytecode member calls final-receiver owned

## Status

Accepted

## Context

Issue
`planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-cffd4a813a`
and PR #2538 integrated the current receiver-aware unified bytecode call
execution lane after the named member-call and computed member-call slices had
already landed separately.

The merged implementation made `CallInvocationBoundary` consume one prepared
stack contract for identifier, named member, and computed member calls. That
removed the identifier-specific invocation helper and made
`PrepareNamedCallTarget` / `PrepareComputedCallTarget` responsible for leaving
the correct receiver next to the loaded callee.

The integration also exposed the important distinction for nested member calls:
`root.child.read()` must bind `this` to `root.child`, not to `root`. Computed
member calls carry a second ordering hazard: nullish receiver errors must be
observed before computed-key coercion, while accepted keys still use normal
property-key conversion when the receiver is valid.

## Decision

Keep direct production unified-bytecode member calls final-receiver owned.

1. Direct named and computed member calls may route through production unified
   bytecode only when the receiver chain is activation-resolved and the
   arguments are simple literal or slot operands.
2. The receiver kept for `CallInvocationBoundary` is the final resolved receiver
   object, including accepted shallow named receiver chains such as
   `root.child.read(value)`.
3. `PrepareNamedCallTarget` and `PrepareComputedCallTarget` must leave the
   common `[receiver, callee, args...]` stack shape and must not delegate to
   `ExpressionProgram`, `ExecutionPlanRunner`, AST evaluation, or a host-call
   fallback.
4. Computed member calls must preserve receiver/key ordering: nullish receiver
   errors occur before key coercion, and valid receivers still perform key
   conversion and property lookup through the context-aware runtime helper
   path.
5. Optional calls, super/construct calls, direct eval, spread calls,
   private/super member targets, dynamic lookup, complex receiver or key
   shapes, and arguments-object dependencies remain pre-VM declines until a
   later slice owns those semantics end to end.

## Consequences

- The member-call production lane is not just "calls are executable"; it is an
  owned receiver/callee stack contract with final-receiver behavior proven by
  public invocation tests.
- Future widening must prove both accepted routing and adjacent no-route
  behavior. Eligibility-only tests are not enough for receiver-sensitive call
  work.
- The next call-family work stays limited to the explicitly deferred families:
  wider eval, spread, construct/super, optional call, dynamic lookup, and
  complex receiver/key shapes.
- This ADR does not claim a CPU or allocation win. It records the semantic
  ownership boundary from the merged receiver-aware call execution slice.

## Evidence

- PR #2538 merged as commit
  `f2f84609fd2ae17cf51e89370dd4e99e1228cded`.
- The delivery branch included build-stage commits for receiver-aware call
  execution, computed-call nullish receiver ordering, optional/super decline
  coverage, and the formatter build-back repair.
- Conflict-resolution proof passed the focused unified-bytecode pack with 197
  tests, then `rtk make quality` passed before the delivery lifecycle continued.
- The review build-back repaired import ordering in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`;
  the focused formatter gate and `rtk git diff --check` passed afterward.

## Related

- PR #2538
- Issue
  `planitem-planmanual1779965179415360000-batch-1-receiver-aware-call-execution-boun-cffd4a813a`
- ADR 0250:
  `docs/adrs/0250-keep-unified-bytecode-call-target-prep-boundary-non-executable.md`
- ADR 0261:
  `docs/adrs/0261-keep-unified-bytecode-call-invocation-boundary-plan-sliced-and-deferred.md`
- ADR 0262:
  `docs/adrs/0262-keep-unified-bytecode-named-member-call-receiver-owned.md`
- ADR 0263:
  `docs/adrs/0263-keep-unified-bytecode-computed-member-call-key-and-receiver-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `docs/unified-bytecode-expansion-contract.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeVirtualMachine.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
