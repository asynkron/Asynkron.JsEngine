# ADR 0212: Keep typed module execution helpers JsValue-native

## Status

Accepted

## Context

Issue `autrun-dit4lwg5pkm0-72a23c5a9c` / PR #2260 continued the Unboxer
cleanup of private `object?` carriers in the core runtime.

ADR 0168 had already moved the private `ExecuteProgram` script/eval path to
`JsValue`, but explicitly left the module-body execution surface as a separate
migration target. Before this delivery, `JsEngine` still had private typed
statement/expression helpers that called `ProgramNode.EvaluateProgram(...)` and
returned `object?`. Several typed module and async-module callsites then
converted the result right back to `JsValue` with
`JsValue.FromObjectUnsafe(...)`.

That carrier was not a public facade, host interop, debugger, or diagnostic
boundary. It was private typed execution plumbing for module evaluation, where
the selected callsites already needed JavaScript values. The delivery kept the
remaining public or compatibility `object?` shape as an adapter at the edge,
while moving the core helper path to `EvaluateProgramJsValue(...)`.

The accepted delivery:

- added `ExecuteTypedStatementJsValue(...)` and
  `ExecuteTypedExpressionJsValue(...)`;
- made the existing `object?` helpers adapt from the typed result through a
  single legacy conversion helper;
- moved selected async module callsites off immediate
  `ExecuteTypedExpression(...)` plus `JsValue.FromObjectUnsafe(...)` rewraps;
  and
- marked `TypedAstEvaluator.EvaluateProgram(object?)` obsolete with
  `error: true` after the selected direct core usage was removed.

Focused evidence from the build and review stages included:

```text
baseline rg FromObjectUnsafe(_engine.ExecuteTypedExpression|program.EvaluateProgram( in JsEngine.cs = 9 matches
final    rg FromObjectUnsafe(_engine.ExecuteTypedExpression|program.EvaluateProgram( in JsEngine.cs = 8 matches
```

Both build and review reran:

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -v minimal
```

and review also confirmed that `program.EvaluateProgram(` no longer appears in
`JsEngine.cs`.

Follow-up issue `autrun-ditfw6gh2qag-7ade4a3977` / PR #2364 completed the
same typed-expression bridge cleanup in `JsEngine.cs`. The remaining private
`ExecuteTypedExpression(...)` `object?` adapter had no intentional public,
host-interop, debugger, or diagnostic role; it only fed module and async-module
callsites that already consumed `JsValue`. The follow-up migrated those
callsites to `ExecuteTypedExpressionJsValue(...)`, assigned default-export
expression bindings directly as `JsValue`, and deleted the private adapter.

Focused evidence from that follow-up:

```text
baseline rg "ExecuteTypedExpression\(" src/Asynkron.JsEngine/JsEngine.cs = 14 matches
final    rg "ExecuteTypedExpression\(" src/Asynkron.JsEngine/JsEngine.cs = 0 matches
```

The build stage also ran:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ModuleTests|FullyQualifiedName~AsyncModuleTryAwaitTests"
```

with 56 passing tests, and review reported the repo quality gate clean.

## Decision

Keep private typed module execution helpers `JsValue`-native.

For future typed module execution migrations:

1. route private typed statement/expression helpers through
   `ProgramNode.EvaluateProgramJsValue(...)`;
2. keep `object?` adapters only at public facade or explicitly deferred
   compatibility boundaries;
3. do not call a private `object?` typed execution helper and immediately rewrap
   the result with `JsValue.FromObjectUnsafe(...)`;
4. do not reintroduce a private `ExecuteTypedExpression(...)` `object?` adapter;
   `ExecuteTypedExpressionJsValue(...)` is the core typed-expression entrypoint;
5. use `[Obsolete(..., true)]` on legacy private wrappers after the selected
   direct usage is removed, so hidden core callsites become compiler errors
   instead of new accidental object-carrier seams;
6. keep module `LastValue` storage and other remaining object-shaped module
   result surfaces as separate focused migration slices unless that owner
   surface is explicitly selected; and
7. prove each slice with a before/after search for the selected legacy
   signatures plus focused module or async-module coverage when behavior, not
   just helper plumbing, changes.

## Consequences

- Typed module execution now follows the same value-primitive direction as the
  private script/eval `ExecuteProgram` path from ADR 0168.
- The selected `ExecuteTypedExpression(...)` private object adapter is gone; a
  future reintroduction should be treated as a regression unless it is tied to
  a new explicit public, host interop, debugger, or diagnostic boundary.
- Remaining `object?` module result storage is visible as deferred work instead
  of being hidden behind private typed execution wrappers.
- Future Unboxer slices should focus on other object-shaped module result
  surfaces without reopening the public `Evaluate*` facade shape.
- Obsolete error-level wrappers are useful as temporary compiler pressure, but
  should not become permanent compatibility APIs once internal callers are gone.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0168-keep-executeprogram-jsvalue-native.md`
- `docs/adrs/0182-keep-module-namespace-own-keys-jsvalue-native.md`
