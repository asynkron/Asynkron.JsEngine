# ADR 0336: Admit resumable class literal public accessors through shared class creation

## Status

Accepted

## Context

ADR 0306 admitted `LoadClassLiteral` through the existing class-definition
machinery, and later B24 slices admitted narrow resumable class-literal families
by proving which class-definition environment each family needs.

B24g needed the same decision for public instance getters and setters inside
generator and async bodies. Public accessors are descriptor-producing class
members: their getter/setter functions must preserve class-definition descriptor
flags and receiver binding, but their bodies can also read `super` or capture a
resumable activation binding. Captured activation bindings live in
`UnifiedBytecodeResumeState` flat slots, not in the captured calling environment
used by class-definition creation, and accessor `super` depends on member-super
state that belongs to the later B24i slice.

## Decision

Admit the B24g public accessor class-literal subset through
`UnifiedBytecodeOpCode.LoadClassLiteral` on the resumable route when all of
these are true:

- every class member is a public non-computed instance getter or setter;
- the class has no fields, static blocks/elements, static accessors, computed
  accessors, private accessors, or mixed neighboring member families;
- no accessor body captures a resumable activation binding;
- no accessor body uses `super`.

The resumable VM continues to execute class creation through
`TypedAstEvaluator.CreateClassValueFromLiteral(...)` with the captured calling
environment. That keeps getter/setter descriptor creation, enumerable and
configurable flags, and receiver-sensitive invocation on the existing
class-definition machinery instead of duplicating accessor semantics in the VM.

Static, computed, private, mixed, activation-capturing, and `super`-using
accessor shapes remain pre-VM declines for B24h/B24i or a future materialized
body-environment route.

## Consequences

- Public non-computed instance getter/setter class expressions can route through
  resumable production unified bytecode for generator and async bodies.
- The VM does not duplicate public accessor descriptor or receiver semantics.
- Future B24 widening must keep accessor admission tied to the exact
  class-definition state that the captured calling environment owns, and pair
  positive route proof with no-route proof for activation-capturing and
  member-super neighbors.
- Progress ledgers must be updated in the same slice and rechecked during
  review: PR #3239 needed a docs-alignment repair after the burndown checklist
  footer still described an older B36/latest-slice state.

## Evidence

- Delivery PR #3239 merged as squash commit
  `eb2a1aac6d1ae54760514d940c83467d9391dd82`.
- Delivery commits before squash:
  - `0495c9e24 Admit B24g resumable class accessors`
  - `5b84b9a87 Align B24 class-literal boundary tests`
  - `977d556b7 Fix B24g burndown checklist totals`
  - `bdce98938 Align B24g burndown checklist footer`
- Implementation changed
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`.
- Focused proof lives in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassExpressionTests.cs`,
  including generator and async `LoadClassLiteral` eligibility, generator and
  async fast-path runtime behavior, descriptor flag and receiver-sensitive
  getter/setter assertions, and no-route pins for computed, static, capturing,
  `super`, and private accessor neighbors.
- Delivery docs updated `docs/bytecode-progress.md`,
  `docs/unified-bytecode-expansion-contract.md`,
  `docs/rules/unified-bytecode-prototypes.md`, and
  `docs/plans/bytecode-burndown-checklist.md` to record B24g as complete and
  the checklist as `99 / 145`.

## Related

- `docs/adrs/0306-admit-class-literals-in-unified-bytecode-through-shared-class-creation.md`
- `docs/adrs/0327-admit-resumable-class-literal-private-members-through-shared-class-creation.md`
- `docs/adrs/0328-admit-resumable-class-literal-public-instance-fields-with-activation-safe-initializers.md`
- `docs/adrs/0329-admit-resumable-class-literal-static-fields-with-owned-environments.md`
- `docs/rules/unified-bytecode-prototypes.md`
