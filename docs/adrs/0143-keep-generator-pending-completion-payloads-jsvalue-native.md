# ADR 0143: Keep generator pending completion payloads JsValue-native

## Status

Accepted

## Context

Issue `autrun-dis251ifyqog-d9eb7698a5` / PR #1934 continued the recurring
object-carrier cleanup in generator `try` / `finally` completion handling.

Before the delivery, `GeneratorPendingCompletion.Value` was stored as `object?`
even though the only saved payloads were `JsValue` return values and
`ThrowFlowCompletionSignal.JsValue`. Restoring the pending completion then had
to defend against the untyped carrier with:

```csharp
pending.Value is JsValue pjs ? pjs : JsValue.FromObjectUnsafe(pending.Value)
```

That fallback did not preserve a public, host interop, debugger, or diagnostic
boundary. It only converted a private JavaScript completion payload out of and
back into the engine value primitive while `finally` executed.

The accepted delivery changed `GeneratorPendingCompletion.Value` to `JsValue`,
initialized and reset it with `JsValue.Undefined`, and restored pending throw or
return completions directly with `context.SetThrow(pending.Value)` and
`context.SetReturn(pending.Value)`.

## Decision

Keep generator pending completion payload storage `JsValue`-native.

When a generator `try` / `finally` path saves a pending return or throw
completion while evaluating `finally`, store the payload as `JsValue` from
capture through restore and reset. Do not reintroduce `object?` storage or a
`JsValue.FromObjectUnsafe(...)` fallback for this private completion carrier
unless a future change creates and proves an explicit host/diagnostic boundary.

Use canonical `JsValue` sentinels such as `JsValue.Undefined` when clearing the
private carrier instead of `null`, because the stored value slot represents a
JavaScript payload and is guarded separately by `HasValue`.

## Consequences

- Future generator completion work should treat private pending return/throw
  payload carriers as JavaScript values, not CLR object carriers.
- The focused proof shape for this boundary is a legacy-carrier search such as
  `rtk rg -n "object\\? Value|pending\\.Value is JsValue|JsValue\\.FromObjectUnsafe\\(pending\\.Value\\)" src/Asynkron.JsEngine/Ast`
  paired with nested generator `try` / `finally` return and throw tests.
- This ADR does not change public facade, host interop, debugger, parser, weak
  collection, or diagnostic `object?` compatibility surfaces.
- This complements `.claude/rules/jsvalue-core-values.md` and ADR 0139's
  broader `finally` completion ordering boundary. ADR 0143 owns only the
  generator pending-completion payload carrier shape.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0139-keep-tail-restarts-through-expression-branches-and-finally-completions.md`
