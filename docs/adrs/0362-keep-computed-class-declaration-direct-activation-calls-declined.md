# ADR 0362: Keep computed class declaration direct activation calls declined

## Status

Accepted

## Context

Faktorial issue #gh3354 repaired red `main` after delivery PR #3349 widened
B24h computed class-name admission for direct zero-argument activation calls.

ADR 0360 admitted class-expression literals such as `[read()]` when the B24h
scanner proves and consumes exactly the adjacent `LoadIdentifierCallTarget` plus
zero-argument `Call` operation pair. That widening was correct for class
expression literals, where the resumable route emits `LoadClassLiteral` and the
existing class-definition machinery evaluates the computed-name expression
program with the resumable calling environment available.

The same helper was then shared by class declarations. That accidentally let a
class declaration with a computed name direct activation call become production
eligible:

```js
function* g(read) {
    yield "ready";
    class Box {
        [read()]() {
            return 1;
        }
    }
    yield typeof Box;
}
```

The class-declaration route is a different ownership boundary. It emits
`DeclareClass`, participates in declaration binding and class-definition
environment setup, and still uses the existing B24h decline text for
activation-dependent computed names until that class-definition environment
route is explicitly owned and proved.

## Decision

Keep B24h direct activation-call computed-name admission limited to class
expression literals.

- Class-expression literal B24h scans may pass `allowDirectActivationCall:
  true` so the dependency scanner can skip only the adjacent activation
  call-target plus zero-argument `Call` pair.
- Class-declaration B24h scans must pass `allowDirectActivationCall: false`.
  A computed declaration name that resolves through a direct activation call
  remains declined before VM execution with the existing B24h dependency reason.
- Future class-declaration widening must prove the declaration binding and
  class-definition environment route directly; it should not inherit a
  class-expression literal scanner exception by sharing a helper default.

This keeps the B24h direct-call skip as a literal-specific exception, not a
generic computed class-name dependency waiver.

## Consequences

- Class-expression computed names such as `[read()]` keep the route admitted by
  ADR 0360.
- Class declarations with computed-name direct activation calls remain on the
  existing IR route until a future slice owns their declaration environment
  semantics.
- Shared dependency scanners that serve both class literals and declarations
  need an explicit route-family parameter for any call-target skip or similar
  exception.

## Evidence

- Delivery PR #3356 merged as squash commit
  `d4dbf5d3c7e97e1c77099cf0d1d9415b5545038c`.
- Delivery branch commit `32da5add8` changed
  `UnifiedBytecodeProductionEligibility.ExpressionProgramHasUnsupportedClassComputedNameActivationDependency`
  to accept an `allowDirectActivationCall` parameter.
- Class-declaration B24h callers now pass `allowDirectActivationCall: false`.
- Class-expression literal B24h callers now pass `allowDirectActivationCall:
  true`.
- Focused proof recorded by the build stage: the class declaration/expression
  pack passed with 58 tests, and `rtk git diff --check origin/main..HEAD`
  passed.
- Learn-stage ADR allocation note: local `rtk faktorial-api adr-next` was not
  present in this worker (`No such file or directory`), so this pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":362}`. The prefix `0362` was checked free before writing.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- ADR 0360:
  `docs/adrs/0360-admit-direct-activation-calls-in-computed-class-names.md`
