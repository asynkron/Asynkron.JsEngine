# ADR 0203: Keep runner symbol stores JsValue-native

## Status

Accepted

## Context

Issue `autrun-disvllqpjvx4-8e3b36a053` / PR #2215 continued the Unboxer
cleanup of private `object?` carriers in the execution-plan runner.

Before the delivery, the runner had a private compatibility helper:

```csharp
StoreSymbolValue(JsEnvironment environment, Symbol symbol, object? value)
```

The helper accepted arbitrary CLR payloads, defensively checked whether the
payload was already a `JsValue`, and otherwise routed through
`JsValue.FromObjectUnsafe(...)` before delegating to the real symbol-store path.
That made the symbol-store API look like an intentional object-carrier boundary
even though most callsites already had JavaScript values.

The accepted delivery removed the `object?` helper and migrated the four known
callsites:

- `yield*` result-slot stores that already held `JsValue` values now call
  `StoreSymbolValueJsValue(...)` directly.
- The `YieldStarState` runtime state object is still stored through a
  `JsValue`, but the object wrapping is explicit at the callsite with
  `JsValue.FromObjectUnsafe(yieldStarState)`.
- The dynamic `with` scope `JsEnvironment` state object is also wrapped
  explicitly at the callsite with `JsValue.FromObjectUnsafe(withEnv)`.

Focused proof included:

```bash
rtk rg -n "StoreSymbolValue\(" src/Asynkron.JsEngine/Ast src/Asynkron.JsEngine/Execution --glob '!bin/**' --glob '!obj/**'
rtk rg -n "object\?" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk dotnet build
rtk make quality
```

The final `StoreSymbolValue(` scan had no matches, the runner `object?` scan no
longer included the removed helper seam, `rtk dotnet build` passed, and review
ran `rtk make quality` successfully.

## Decision

Keep execution-plan runner symbol stores `JsValue`-native.

When a runner helper stores a value by `Symbol`, the helper contract should
accept `JsValue` and forward to `JsEnvironment`'s `JsValue` storage APIs. Do not
reintroduce an `object?` compatibility helper that decides whether to wrap,
unwrap, or preserve payloads.

If the symbol slot intentionally carries a runtime state object rather than a
JavaScript value payload, make that boundary explicit at the callsite with
`JsValue.FromObjectUnsafe(...)` and keep the paired read typed with
`TryGetObject<T>(...)`. This keeps state-object exceptions visible in targeted
`object?`/`FromObjectUnsafe` audits while leaving ordinary JavaScript value flow
on the `JsValue` path.

## Consequences

- Future runner symbol-store work should route existing `JsValue` payloads
  directly through `StoreSymbolValueJsValue(...)` or an equivalent typed helper.
- Intentional internal state carriers such as generator, iterator, await, or
  `with` bookkeeping objects remain allowed, but their wrapping should be local
  and explicit rather than hidden behind a generic `object?` store helper.
- The focused proof shape for this boundary is a before/after search for
  `StoreSymbolValue(` in runner/IR-owned code, plus a runner-scoped `object?`
  scan that distinguishes intentional state-object or public/debug surfaces from
  legacy JavaScript value carriers.
- This ADR does not change public facade returns, host interop, debugger,
  parser token literals, equality overrides, weak-key identity surfaces, or
  diagnostic object projections.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0143-keep-generator-pending-completion-payloads-jsvalue-native.md`
- `docs/adrs/0168-keep-executeprogram-jsvalue-native.md`
