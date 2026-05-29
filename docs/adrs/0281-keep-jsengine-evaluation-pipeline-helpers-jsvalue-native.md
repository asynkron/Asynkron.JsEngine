# ADR 0281: Keep JsEngine evaluation pipeline helpers JsValue-native

## Status

Accepted

## Context

Issue `autrun-diuvt2ksxyuw-47f4dafb7f` / PR #2635 removed `ConvertJsValueToLegacyObject`
and migrated the private evaluation pipeline helpers in `JsEngine.cs` from `object?` to `JsValue`.

The affected helpers were:
- `EvaluateInline(...)` return type: `object?` → `JsValue`
- `EvaluateSyncInternal(...)` return type: `object?` → `JsValue`
- `EnsureModuleEvaluatedAsync(...)` return type: `Task<object?>` → `Task<JsValue>`
- `CompleteEvaluationAfterSynchronousExecution(object? result, ...)` → `(JsValue result, ...)`
- `DrainPendingEventLoopAndCompleteAsync(object? result, ...)` → `(JsValue result, ...)`
- `UnwrapResult(object?)` → `UnwrapResult(JsValue)`

All callers of these helpers lived inside the private evaluation pipeline and only needed `JsValue`. The sole reason for the `object?` return types was the legacy `ConvertJsValueToLegacyObject` bridge, which converted `entry.LastValue` (already a `JsValue`) back to an object carrier, only for callers to later wrap it again with `JsValue.FromObjectUnsafe(...)`.

The public `Evaluate*` facades retained their `object?` return types and now call `.ToObject()` at the API edge.

A private `SetGlobal(string, object?)` bridge overload must remain because generated code produced by `[JsConstructor]` attributes returns `IJsObjectLike` without an implicit conversion to `JsValue`. That overload delegates to `JsValue.FromObjectUnsafe()` and is the intentional host-interop boundary for code-generated callers.

## Decision

Keep private evaluation-pipeline helpers in `JsEngine.cs` typed as `JsValue` end to end.
Convert to `object?` only at explicit public API edges.
Delete private conversion bridge helpers (`ConvertJsValueToLegacyObject`) that have no legitimate object-carrier callers once the pipeline is fully typed.
Keep `SetGlobal(string, object?)` as an intentional host/generated-code bridge overload that cannot be removed without changing code generation.

## Consequences

Private evaluation helpers pass JavaScript values through without boxing. Public API callers continue to receive `object?` from `Evaluate*` facades. The `SetGlobal(string, object?)` private overload remains as an explicit, non-obsolete host-interop bridge for generated callers.
