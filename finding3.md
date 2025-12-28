## Private fields fail in arrow functions (fast path skips private scopes)

- Fast-path invocations in `TypedFunction` (`InvokeSimpleFast*`/`InvokeSimpleFastCore*`) never enter `EvaluationContext.EnterPrivateNameScope` or `EnterPrivateNameScopes`. The slow path does, so only fast path runs with an empty private-scope stack.
- Private scopes are attached to callables only when they are created. `FunctionExpressionExtensions.CreateFunctionValue` captures `context.CapturePrivateNameScopes()` and calls `SetPrivateNameScope`/`SetCapturedPrivateNameScopes`. If the creator is running on the fast path, the stack is empty and those setters never disable fast path (`_canUseFastPathBase` stays true).
- Private field access resolves via `PropertyHandle.ResolvePrivate` → `context.ResolvePrivateNameKey`/`context.CurrentPrivateNameScope`; if the stack is empty, access throws (“Invalid access of private member”).
- Repro: class method that remains fast-path-eligible creates an inner arrow accessing `this.#x`; the arrow captures no private scopes, still takes fast path, and fails to find the brand. See `tests/Asynkron.JsEngine.Tests/PrivateFieldsTests.cs::PrivateField_ArrowFunction_DebugTest` (cases 2b–4).

### Code references
- Slow path pushes private scopes:
  - `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedFunction.cs` (`InvokeWithContextSlow`): enters `_capturedPrivateNameScopes` and `PrivateNameScope`.
- Fast path does not push scopes:
  - `...InvokeSimpleFastCore*` in `TypedFunction.cs`: no `EnterPrivateNameScope`/`EnterPrivateNameScopes`; only a debug guard that throws if scopes are present.
- Private scope capture at function creation:
  - `src/Asynkron.JsEngine/Ast/FunctionExpressionExtensions.cs` (`CreateFunctionValue`): uses `CapturePrivateNameScopes` and `SetPrivateNameScope`/`SetCapturedPrivateNameScopes`.
- Private lookup failure surface:
  - `src/Asynkron.JsEngine/Ast/PropertyHandle.cs` (`ResolvePrivate`): throws when no current/captured private scope.

### Status
- No code changes made; this file documents the bug surface and repro.

