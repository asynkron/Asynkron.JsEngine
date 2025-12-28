## Bug: Class-defined arrow functions lose private-field access on fast path

- **Root cause**: Fast-path invocation (`InvokeSimpleFast*` / `InvokeSimpleFastCore*` in `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.TypedFunction.cs`) never calls `EvaluationContext.EnterPrivateNameScope(s)`, so the private-name stack stays empty during fast-path execution.
- **Capture gap**: `_canUseFastPathBase` is only disabled when `SetPrivateNameScope` / `SetCapturedPrivateNameScopes` see a non-empty scope list. If a function is created while the context has no private scopes pushed (e.g., its creator ran on the fast path), `CapturePrivateNameScopes` returns empty, `_canUseFastPathBase` stays true, and the function remains eligible for fast path with no scope push.
- **Failure mode**: A “simple” class method hits the fast path, creates an inner arrow that touches `#private`. The arrow captures an empty private-scope list. Later `this.#field` brand lookup finds no scope and fails.
- **Evidence**:
  - Slow path pushes scopes:
    - `TypedAstEvaluator.TypedFunction.InvokeWithContextSlow` uses:
      ```
      using var capturedPrivateScopes = !_capturedPrivateNameScopes.IsDefaultOrEmpty
          ? context.EnterPrivateNameScopes(_capturedPrivateNameScopes)
          : null;
      using var privateScope = PrivateNameScope is not null
          ? context.EnterPrivateNameScope(PrivateNameScope)
          : null;
      ```
  - Fast path omits scope push:
    - `InvokeSimpleFast*`/`InvokeSimpleFastCore*` rent context/env, bind params, execute body—no `EnterPrivateNameScope(s)`.
  - `_canUseFastPathBase` gating:
    - Setters flip it to false only when scopes are non-empty; otherwise fast path remains enabled.
- **Repro context**: `tests/Asynkron.JsEngine.Tests/PrivateFieldsTests.cs::PrivateField_ArrowFunction_DebugTest` (cases 2b–4) exercises arrows accessing private fields.
- **Code changes**: None yet; issue is documented here.

