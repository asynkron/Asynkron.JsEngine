# ADR 0356: Admit object method literal spans through mirrored compiler emission

## Status

Accepted

## Context

Issue `planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-03-com-d8620d63a8`
targeted A51h, the remaining unified-bytecode compiler diagnostics bucket for
array/object/template literal spans, spread sources, computed object keys, and
simple literal-span shapes.

The selected residual was an object literal used as a first-boundary RHS where
the object contained a shorthand method or accessor and then continued with a
later member, especially a trailing spread:

```js
function compare(extra) {
    return this.value === { m() { return 1; }, ...extra };
}
```

Eligibility already had an object-literal span measurer for method/accessor
members, so the route could be selected. The compiler span helper did not yet
mirror that vocabulary inside `TryAppendSimpleObjectLiteralSpan`. That created a
compiler mismatch: selection could reach the production compiler, but emission
could not append the same member sequence.

## Decision

Admit object-literal method and accessor members inside simple object literal
spans by mirroring the eligibility span grammar in the compiler.

- Static method/accessor members compile as:
  `LoadFunctionLiteral`, then `DefineObjectMethod` or `DefineObjectAccessor`.
- Computed method/accessor members compile as:
  `<key span>`, `ResolvePropertyKey`, `LoadFunctionLiteral`, then
  `DefineComputedObjectMethod` or `DefineComputedObjectAccessor`.
- Function literals are added to `FunctionLiteralConstants` and emitted through
  `LoadFunctionLiteral`; method/accessor definitions stay VM-owned object-member
  opcodes.
- Computed-member probing rolls back unified instructions, literal constants,
  string constants, and function-literal constants if the full member shape is
  not accepted.
- The compiler stack estimate accounts for expanded object function-member
  sequences so later spread/object member operations keep enough stack space.

A51h remains open. This delivery closes only the covered compiler mismatch for
object method/accessor members inside a first-boundary object-literal RHS span;
broader literal/span compiler leaves stay tracked in the checklist.

## Consequences

- The production route can execute the covered object-method-plus-trailing-spread
  shape without falling back to expression-program evaluation.
- Eligibility and compiler span helpers now share the same object
  method/accessor member vocabulary for this literal-span context.
- Future A51h literal-span admissions should treat method/accessor widening as a
  coupled compiler surface: span measurement, append emission, function-literal
  constants, rollback, object-member opcodes, stack sizing, and focused route
  proof.
- This does not admit unrelated function-literal capture, name-inference,
  private-name, or arbitrary computed-key shapes. Those remain owned by their
  existing decline families.

## Evidence

- Delivery PR #3340 merged as commit `49d481264`:
  `Admit object method literal spans (#3340)`.
- Build-stage proof passed `rtk dotnet build`.
- Focused runtime proof passed
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionObjectSpreadNonSimpleSourceTests|FullyQualifiedName~AlreadyRoutingShapePinTests.A35"`
  with 14 tests.
- `rtk git diff --check` passed.
- The focused AST seam scan for `EvaluateExpression(` /
  `ProfileEvaluateExpression(` in runner files found no matches.
- Learn-stage ADR allocation note: the local `rtk faktorial-api adr-next`
  helper was unavailable in this runtime (`No such file or directory`), so this
  pass used the runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":356}`.

## Related

- `docs/adrs/0290-admit-array-and-object-literals-in-unified-bytecode-simple-span-measurement.md`
- `docs/adrs/0312-keep-object-spread-out-of-computed-key-production-routing.md`
- `docs/rules/unified-bytecode-prototypes.md`
