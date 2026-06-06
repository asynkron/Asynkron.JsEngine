# ADR 0350: Keep resumable scoped closure literals declined until block environments are owned

## Status

Accepted

## Context

Issue #gh3304 and PR #3307 repaired red `main` after resumable production
eligibility admitted a generator shape where a nested function literal captured
a per-iteration `const` binding:

```js
function* g(values) {
    for (const v of values) {
        yield () => v;
    }
}
```

The existing captured-root-local route from ADR 0333 was not enough for this
shape. ADR 0333 materializes the generator body environment and synchronizes
activation slots across suspension. A per-iteration or block-scoped binding is
owned by a `PushEnvironment` / block environment lifetime instead. Letting the
body-environment proof cover that closure would either bind the closure to the
wrong environment or miss the per-iteration binding identity that JavaScript
requires.

The same repair also showed an adjacent false decline: a computed class member
name that reads an activation binding can still route when the shared
class-literal machinery already owns that creation path. The safety boundary is
not "any activation read in a literal declines"; it is whether the created
function closes over an environment lifetime the resumable route has not
materialized.

## Decision

Keep nested function literals that capture scoped bindings outside the root
activation declined on the resumable unified-bytecode route until the VM owns
materialized block environments across suspension.

- Scan nested function-literal bodies for identifier references that resolve
  outside the nested function's own activation but are not part of the outer
  resumable activation slot shape.
- Decline those shapes with a scoped-binding reason before VM execution.
- Keep root activation captures on the existing materialized body-environment
  path from ADR 0333.
- Keep class-literal computed activation-name shapes admitted when the shared
  class creation path owns the creation semantics and no scoped closure
  lifetime is involved.
- Treat unknown nested plan or activation-slot analysis as a decline, not as a
  routeable shape.

## Consequences

- Future resumable closure widening must distinguish root body bindings from
  block, catch, and per-iteration bindings.
- A materialized body environment is not a general closure-environment bridge.
  Block environments need their own lifetime, slot synchronization, and
  suspension/resume proof.
- Route tests for nested literals should include both admitted root activation
  captures and declined scoped captures so the selector cannot regress toward a
  name-only activation-read gate.
- Class-literal B24 work should keep computed activation-name admission
  separate from closure-valued member/static initializer captures.

## Evidence

- PR #3307 merged as squash commit
  `377ccf62e564cc37f9fcc060b9a665a4273cd798`.
- Delivery commit before squash:
  `1a5ad4518 fix resumable scoped closure eligibility`.
- Implementation changed
  `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`.
- Focused proof lives in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableNestedFunctionTests.cs`
  for the scoped per-iteration closure decline, and in
  `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableLiteralTests.cs` for
  the computed class-literal activation-name admission.
- Build-stage verification recorded the targeted 9-test filter passing, the
  owning resumable nested/literal/class-expression packs passing 109 tests, the
  exact red-main command passing 6495 tests, and `rtk git diff --check`
  passing.

## Related

- ADR 0333:
  `docs/adrs/0333-admit-generator-captured-function-literals-through-materialized-resumable-body-environment.md`
- ADR 0334:
  `docs/adrs/0334-admit-captured-per-iteration-bindings-through-push-environment-copy-slots.md`
- ADR 0335:
  `docs/adrs/0335-admit-generator-captured-hoisted-helpers-through-materialized-body-environment.md`
- `docs/rules/unified-bytecode-prototypes.md`
