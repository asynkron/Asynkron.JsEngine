# JavaScript Spec Property Access in C# Helpers

When implementing ECMAScript algorithms that say `Get(O, P)`, use a runtime
property access path that observes inherited properties, accessors, proxies, and
JavaScript throws. Do not replace `Get` with own-element storage reads just
because the receiver is array-like.

## Nullable Throw State

If the access helper accepts an optional `EvaluationContext`, check nullable
throw state explicitly:

```csharp
if (evalContext?.IsThrow is true)
{
    throw new ThrowSignal(evalContext.FlowValue);
}
```

Avoid `== true` for this pattern. It is easier to miss during review and was
the concrete cleanup requested after the issue #751 Array.prototype.at fix.

## Why

Issue #751 fixed `Array.prototype.at` after the direct array-element path failed
Test262 semantics for sparse holes, inherited indexed properties, and throwing
getters. The durable lesson is not specific to `at`: spec-level `Get` must be
implemented as observable JavaScript property access, and the C# nullable
throw-state check must propagate the JavaScript exception immediately.
