# ADR 0218: Keep unified bytecode property-read production boundary selector-owned

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1779860498694736000-batch-1-property-read-boundary-define-and-8d40cdb281`
and PR #2288 defined the first unified bytecode production boundary for
property reads.

The issue intentionally owned the contract, not VM execution. Plain member
reads already lower into `ExpressionProgram` operations: direct named reads use
`LoadIdentifier` followed by `GetNamedProperty`, while direct computed reads
use base/key evaluation followed by `RequireObjectCoercible(Depth: 1)`,
`ResolvePropertyKey`, and `GetComputedProperty`. The risk was letting
`GetNamedProperty`, `GetComputedProperty`, or `ResolvePropertyKey` through by
opcode name before the unified compiler and VM owned the observable JavaScript
semantics for nullish checks, key coercion, accessors, prototypes, and optional
chain short-circuiting.

During delivery, the selector was widened to classify the property-read family
before generic compiler fallback. It also started inspecting expression
programs carried by `EvaluateAndDiscardInstruction` and `ThrowInstruction`, so
property-read hazards are not hidden merely because they do not appear in a
return payload.

## Decision

Keep the first production property-read boundary selector-owned and
decline-first.

- Recognize direct named property-read candidates only when the expression
  program is exactly `LoadIdentifier` from an activation-resolved slot followed
  by a non-optional `GetNamedProperty`.
- Recognize direct computed property-read candidates only when the expression
  program is exactly a direct activation-resolved base load, a production-safe
  identifier-or-literal key load, `RequireObjectCoercible(Depth: 1)`,
  `ResolvePropertyKey`, then non-optional `GetComputedProperty`.
- Include `ResolvePropertyKey` in the computed-read candidate contract. It is
  part of the ordinary non-optional computed read lowering and must stay after
  the object-coercible check.
- Decline recognized named and computed candidates as
  `PropertyReadCandidateRequiresVmSupport` until the same slice adds unified
  compiler opcodes, VM execution semantics, and public invocation route proof.
- Decline adjacent families before VM execution with stable diagnostics:
  calls/constructs and member call targets, writes, updates, delete, `super`
  property access, `this` access, optional-chain short-circuiting, object
  literal/spread, dynamic lookup, and computed-read shapes outside the exact
  boundary.
- Inspect expression programs from return, declaration/assignment payloads,
  evaluate-and-discard payloads, and throw payloads before trying the unified
  compiler, so the selector owns observable hazards consistently.
- Keep the unified VM fallback-free. Do not add a property-read bridge back to
  `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation.

## Consequences

- The production selector can now name property-read candidates without making
  them executable. This gives later VM work a precise positive boundary and
  concrete negative families to preserve.
- ADR 0218 remains the selector/baseline contract. The current executable
  production boundary is documented by ADR 0221 and ADR 0222; do not read this
  ADR alone as the full current runtime boundary.
- Future property-read production widening must move selector acceptance,
  compiler emission, VM semantics, route-priority proof, and negative no-route
  tests together.
- The computed-read ordering from ADR 0119 remains part of the production
  contract: key expression side effects, nullish-base checking, property-key
  conversion, and final read stay in spec order.
- Unsupported property-read-adjacent shapes remain visible as pre-VM declines
  instead of being masked by generic compile failure or mixed execution.

## Evidence

- `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests"`
  passed with 46 tests.
- Selector tests cover accepted-candidate examples for `box.value` and
  `box[key]`, including the computed
  `RequireObjectCoercible(Depth: 1) -> ResolvePropertyKey -> GetComputedProperty`
  sequence.
- Selector tests cover declined source examples for calls/constructs, member
  call targets, writes, updates, delete, `super` property access, `this` access,
  optional chaining, object literal/spread, dynamic lookup, and computed reads
  outside the first boundary.

## Related

- Issue
  `planitem-planmanual1779860498694736000-batch-1-property-read-boundary-define-and-8d40cdb281`
- PR #2288
- Commit `b7cf89aef7a568de792ce0b077840bc0eb7532fa`
- ADR 0119: `docs/adrs/0119-keep-computed-member-nullish-read-order-spec-ordered.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0210: `docs/adrs/0210-keep-unified-bytecode-control-flow-production-routing-operator-and-shape-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
