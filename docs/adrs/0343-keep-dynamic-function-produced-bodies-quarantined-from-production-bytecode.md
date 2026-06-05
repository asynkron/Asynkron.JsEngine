# ADR 0343: Keep dynamic Function produced bodies quarantined from production bytecode

## Status

Accepted

## Context

Faktorial issue
`planitem-planmanual1780639098493226000-full-unified-bytecode-execution-burndown-b-11ca930244`
and delivery PR #3262 closed D4 in the unified-bytecode burndown checklist:
`Function(...)` produced-body quarantine.

Before this delivery, bodies produced by `Function` / `new Function` were parsed
into ordinary `FunctionExpression` nodes and could be selected by the sync
production unified-bytecode invocation route when their body shape otherwise
looked eligible. That blurred two boundaries that future route-widening work
must keep separate:

- constructing a dynamic function object is still a supported dynamic boundary;
- executing the generated body through the most aggressive production bytecode
  route is a separate readiness decision.

The correct closure for D4 was not to change the constructor/call boundary or to
add another runtime fallback. The delivery marked the generated body at the parse
boundary, then declined only that produced body before the sync production
unified-bytecode selector. Ordinary adjacent functions in the same program
remain eligible and still prove that the decline is scoped to the dynamic
origin, not to every call near a dynamic constructor.

## Decision

Keep dynamic `Function` / `new Function` produced bodies explicitly marked and
declined before sync production unified-bytecode invocation until a future
delivery proves that generated-body semantics are owned by that route.

- The origin marker belongs on the produced `FunctionExpression`, not on a
  caller-local source-text heuristic or a generic dynamic-shape predicate.
- The constructor and object-call boundary remains unchanged. The produced
  function can still execute through existing non-production routes.
- The production selector must decline the marked produced body before plan
  shape or activation shortcuts can admit it.
- Ordinary functions adjacent to the dynamic constructor must remain eligible
  when they satisfy the production route. A quarantine proof must include both
  the no-route generated body and a route-hit ordinary neighbor.
- Do not remove the marker or replace it with broad source scans until the
  future admission slice owns generated-body activation, dynamic-global
  semantics, diagnostics, and route proof directly.

## Consequences

- Future dynamic-boundary work can continue to classify `Function` constructors
  as dynamic-but-lowered without claiming that generated bodies are production
  bytecode eligible.
- Route widening around function invocation must inspect origin metadata before
  treating an otherwise route-shaped body as ordinary sync code.
- A test that only asserts the generated function returns the right value is not
  enough for this boundary; it must also assert the production route did not
  fire for the generated body.
- A test that only asserts the generated body did not route is also incomplete;
  it can hide accidental broad declines unless it proves an adjacent ordinary
  function still routes.

## Evidence

- ADR allocation note: the local `rtk faktorial-api adr-next` wrapper was not
  available in this runtime (`No such file or directory`), so this learn pass
  used the local runtime allocator endpoint `POST /api/adrs/next`, which
  returned `{"adr_id":343}`.
- Delivery PR #3262 merged as commit
  `98da6f5061cfa610cef98a6167616ce16b343fe1`.
- The carried delivery commit was
  `23efe8cf7 Quarantine dynamic Function bodies from production bytecode`
  before rebasing this learn branch onto local `origin/main`.
- The delivery changed:
  - `src/Asynkron.JsEngine/Ast/FunctionExpression.cs`
  - `src/Asynkron.JsEngine/StdLib/Function/FunctionConstructor.cs`
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
  - `tests/Asynkron.JsEngine.Tests/DynamicFunctionConstructorQuarantineTests.cs`
  - `docs/plans/bytecode-burndown-checklist.md`
- Build-stage proof recorded:
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~DynamicFunctionConstructorQuarantineTests"` passed 1 test.
  - `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~AstFreeExecutionAssertionTests&FullyQualifiedName~FunctionConstructor"` passed 1 test.
  - `rtk rg "EvaluateExpression\(|ProfileEvaluateExpression\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*` found no matches.
  - `rtk git diff --check` passed.

## Related

- `docs/rules/unified-bytecode-prototypes.md`
- `docs/rules/expression-bytecode-ast-seams.md`
- `docs/plans/bytecode-burndown-checklist.md`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- `src/Asynkron.JsEngine/Ast/FunctionExpression.cs`
- `src/Asynkron.JsEngine/StdLib/Function/FunctionConstructor.cs`
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/DynamicFunctionConstructorQuarantineTests.cs`
