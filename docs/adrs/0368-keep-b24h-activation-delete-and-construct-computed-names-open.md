# ADR 0368: Keep B24h activation delete and construct computed names open

## Status

Accepted

## Context

Issue
`planitem-planitem-planitem-gh3377-rebaseline-the-finite-bytecode-retirement-inven-02ad521859`
and delivery PR #3418 rebaselined B24h computed class-expression proof rows
after the finite bytecode retirement inventory had overclaimed activation
delete and construct computed-name routes.

B24h already admits several public computed class-expression neighbors through
`LoadClassLiteral` when the class-definition environment state is proven. The
stale rows treated activation deletes and activation-dependent constructs as
admitted runtime proofs:

```js
function* g(key) {
    yield "ready";
    var C = class {
        [delete key]() { return 1; }
    };
    return C;
}
```

```js
function* g(MakeName) {
    yield "ready";
    var C = class {
        [new MakeName()]() { return 42; }
    };
    return C;
}
```

Those shapes are not the same as the admitted direct activation-call and
immediate function-literal call boundaries. Delete has reference/delete
semantics for an activation binding, and construct/super-construct needs the
broader activation-dependent call/construct state that the current B24h route
does not own.

## Decision

Keep activation delete and activation-dependent construct computed class names
as open B24h eligibility/no-route proof until a future class-definition
environment slice owns those semantics end to end.

- `DeleteIdentifier` is not an owned B24h computed-name activation operation.
  The selector must decline it before `LoadClassLiteral` with
  `UnsupportedPlanShape` and a reason containing `activation binding delete`.
- Activation-dependent `Construct` and `SuperConstruct` dependencies must
  decline before `LoadClassLiteral` with a reason containing
  `activation-dependent call or construct`.
- The proof manifest must record these shapes as `claim: "open"` and
  `kind: "eligibility"`, not admitted runtime rows.
- Adjacent admitted rows for direct activation calls, bounded call arguments,
  immediate non-async/non-generator function-literal calls, and class body
  materialized-environment routes stay separate; they are not evidence that
  delete or construct dependencies are owned.

## Consequences

- B24h remains a finite mixed lane: admitted subfamilies stay visible, while
  delete and construct residues continue to point at the broader
  class-definition environment bridge.
- Future B24h widening must replace the exact open manifest rows with focused
  positive route proof and nearby no-route proof for still-unowned neighbors.
- Runtime fallback correctness is not enough to classify a proof-manifest row
  as admitted; admitted rows need a production route signal for the same source
  shape.

## Evidence

- ADR allocation note: local `rtk faktorial-api adr-next` was unavailable in
  this worker (`No such file or directory`), so this learn pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":368}`. The prefix `0368` was checked free before writing.
- Delivery PR #3418 merged as commit
  `4b074eeccc455cefa770337672a6f4c2426add10`.
- Delivery changed:
  - `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassExpressionTests.cs`
  - `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeResumableClassDeclarationTests.cs`
  - `docs/plans/bytecode-proof-manifest.json`
  - `docs/bytecode-progress.md`
- The build-stage summary recorded stale B24h activation-delete
  admitted/runtime manifest rows moving from `1` to `0`, plus focused
  `BytecodeProofManifestTests` and activation-delete class-expression tests
  passing.

## Related

- `docs/plans/bytecode-proof-manifest.json`
- `docs/bytecode-progress.md`
- `docs/rules/unified-bytecode-prototypes.md`
- ADR 0360:
  `docs/adrs/0360-admit-direct-activation-calls-in-computed-class-names.md`
- ADR 0367:
  `docs/adrs/0367-keep-b24h-constructor-body-activation-captures-open.md`
