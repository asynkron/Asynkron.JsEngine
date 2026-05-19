# JavaScript Spec Property Access in C# Helpers

When implementing ECMAScript algorithms that say `Get(O, P)`, use a runtime
property access path that observes inherited properties, accessors, proxies, and
JavaScript throws. Do not replace `Get` with own-element storage reads just
because the receiver is array-like.

## Object Environment Writeback

For `with` object-environment bindings, keep the captured binding target and
the current strict/sloppy writeback rules separate:

- direct assignment must capture the object-environment reference before
  evaluating the RHS, then write through that captured reference afterward;
- strict writeback for a captured object binding must re-check `HasProperty`
  before `Set` when missing assignment is not explicitly allowed;
- if a getter, RHS side effect, compound assignment, or update operator deletes
  the binding before writeback, throw `ReferenceError` instead of recreating the
  property through the generic property setter path;
- preserve sloppy-mode recreate-after-delete behavior only through an explicit
  sloppy/allow-missing path.

Add focused tests for both sides when touching this area: strict missing-binding
writeback must throw after the side effect that deletes the binding has already
run, while sloppy captured-binding writeback may recreate the property.

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

Issue #784 / PR #932 fixed strict postfix decrement through a `with` object
environment after the getter deleted the binding before writeback. Issue #785 /
PR #933 confirmed the same binding contract for strict postfix increment. The
generic property setter path could recreate the property, but ECMAScript strict
object-environment `SetMutableBinding` must throw when the binding has
disappeared. The durable lesson is to model object-environment writeback as a
binding operation first and only use property setting after the strict
missing-binding check has passed.

Issue #774 / PR #950 extended that lesson to plain assignment. The RHS can
delete the resolved `with` binding before `PutValue`; strict mode still has to
throw through the captured object-environment reference after RHS side effects,
not fall back to a generic identifier/property assignment path.

Issue #777 confirmed the same object-environment writeback contract for
compound assignment. The compound operator may read through a captured `with`
binding whose getter deletes the property before the final `PutValue`; strict
mode must still re-check the captured object binding and throw `ReferenceError`
instead of letting the generic setter recreate the property per operator.
