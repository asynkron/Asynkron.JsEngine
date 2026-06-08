# ADR 0360: Admit direct activation calls in computed class names

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780730299657353000-unified-bytecode-remaining-burndown-04-cla-c4d613c27d`
/ PR #3349 handled a narrow B24h resumable class-expression gap.

Earlier B24h slices had admitted public computed class elements whose computed
names either avoided resumable activation slots or used owned activation
read/write/update/reference-store operations. Direct computed-name calls such
as `[read()]` still declined because the selector treated
`LoadIdentifierCallTarget` plus any later `Call` as a broad activation-dependent
call family:

```js
function* g(read) {
    yield "ready";
    var C = class {
        [read()]() {
            return 42;
        }
    };
    return new C().value();
}
```

That was too broad for the direct zero-argument activation-call shape. Class
definition creation already evaluates computed-name expression programs through
the existing class machinery with the resumable calling environment available.
The delivery could therefore admit the adjacent activation call-target plus
zero-argument call pair without admitting activation values passed into another
callee, delete, construct, `super` construct, nested captured literals, or
activation-capturing class bodies.

## Decision

Admit B24h computed class names that consist of a direct zero-argument
activation call by consuming only the adjacent `LoadIdentifierCallTarget` and
`Call` operation pair in the computed-name expression program.

- The B24h dependency scan uses indexed traversal so it can skip the exact
  adjacent call pair after proving the call target resolves to an activation
  slot.
- The admission is limited to zero-argument direct activation calls such as
  `[read()]`.
- Calls that pass an activation value into another callee, constructs,
  `SuperConstruct`, activation deletes, nested computed-name literals that
  capture activation slots, activation-capturing field initializers, and
  capturing constructor/member bodies stay declined before VM execution.
- Positive proof must pair selector admission with a public resumable generator
  route hit. Nearby negative proof must keep at least activation-call arguments
  and activation deletes off the route.

This keeps B24h as a class-definition state admission, not a generic
activation-call widening.

## Consequences

- Direct activation-call computed class names can route through
  `LoadClassLiteral` on the resumable generator path when the rest of the class
  literal satisfies the existing B24h activation-safety rules.
- Future B24h widening must classify each remaining computed-name dependency by
  the state it needs: direct call-target pairs, activation argument flow,
  deletion/reference semantics, construct/super construct, nested capture, and
  class body capture are separate boundaries.
- The computed-name scanner must remain adjacency-aware. A future helper that
  skips a call dependency must prove the exact operation sequence it consumes so
  it does not hide later unsupported operations.

## Evidence

- Delivery PR #3349 merged as squash commit
  `b2606779e2d2532c3b2d14cb0cee156416556ae8`.
- Delivery branch commit `16e82f7fb` changed
  `UnifiedBytecodeProductionEligibility.ExpressionProgramHasUnsupportedClassComputedNameActivationDependency`
  from `EnumerateOperations()` traversal to indexed traversal and added
  `TrySkipDirectClassComputedNameActivationCall`.
- Focused proof added
  `EvaluateResumable_ClassExpressionComputedNameDirectActivationCall_AdmitLoadClassLiteral`,
  `GeneratorComputedPublicInstanceActivationCall_RouteResumableAndResolveName`,
  and
  the then-current activation-call-argument decline proof, later superseded by
  `EvaluateResumable_ClassExpressionComputedNameActivationCallArgument_AdmitLoadClassLiteral`
  when the bounded argument region became admitted.
- Later rebaseline work corrected the activation-delete neighbor back to an
  open no-route boundary; see ADR 0368. The remaining nearby decline proof
  includes activation delete, activation-dependent construct, and
  nested-capture class-definition environment rows.
- Build-stage verification recorded focused
  `UnifiedBytecodeResumableClassExpressionTests` with 50 passed,
  `rtk git diff --check` clean, no runner AST-eval seam matches, and
  `rtk ./tools/profile forloop --memory` at 6.96 MB.
- Learn-stage ADR allocation note: local `rtk faktorial-api adr-next` was not
  present in this worker (`No such file or directory`), so this pass used the
  runtime allocator endpoint `POST /api/adrs/next`, which returned
  `{"adr_id":360}`. The prefix `0360` was checked free before writing.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/bytecode-progress.md`
- `docs/unified-bytecode-expansion-contract.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0359:
  `docs/adrs/0359-admit-nested-simple-operand-spans-through-bounded-recursive-walkers.md`
- ADR 0368:
  `docs/adrs/0368-keep-b24h-activation-delete-and-construct-computed-names-open.md`
