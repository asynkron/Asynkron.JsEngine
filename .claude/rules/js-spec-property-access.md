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
- simple `var` declarations with initializers must also resolve the binding
  reference before evaluating the initializer. If the initializer mutates the
  `with` lookup surface, write back through the pre-resolved reference rather
  than resolving the name again after the initializer.

Add focused tests for both sides when touching this area: strict missing-binding
writeback must throw after the side effect that deletes the binding has already
run, while sloppy captured-binding writeback may recreate the property.

## Destructuring Binding Target Order

For compiled object destructuring binding, keep the observable ordering explicit:

- evaluate any computed source property name first;
- resolve or capture the binding target at the binding-target step;
- only then read the source property and evaluate a default initializer;
- write through the captured binding target when one was resolved.

This is not just an optimization detail. `with` environments can observe target
lookup through `has`, source properties can observe getter side effects, and
defaults can observe later name lookups. Do not move var target lookup after
source property access/default evaluation, and do not repair this class of bug
by adding an AST-evaluation fallback to the compiled binding runner.

## Super Property Reference Order

For expression bytecode that touches `super.property` or `super[expr]`, keep the
super-reference validation before any observable property-key work:

- emit or execute `EnsureSuperReference` before evaluating computed property
  keys;
- only evaluate `super[expr]` keys after the derived constructor has initialized
  `this`;
- keep the final operation-specific error, such as delete-super
  `ReferenceError`, after the key side effects that are valid for an initialized
  `super` reference.

This applies even when the operation always throws. The throw does not erase the
observable ordering before it.

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

## Native Reentrancy Guards

Keep native reentrancy guards out of JavaScript-visible property storage.

When a built-in such as an Array prototype mutator needs to prevent recursion
from an observable getter, setter, proxy trap, or callback, store the guard in
private runtime state keyed by the receiver/accessor identity and clear it in
`finally`. Do not write marker properties such as `__inPush__` onto arrays or
array-like receivers.

These guard writes are not harmless implementation details. JavaScript can
observe them through `HasProperty`, `Get`, `Set`, enumeration, proxy traps, and
array length interactions.

## Built-In Copy Operations Use Set

When a built-in copies values into an existing target object, preserve the
spec's write operation. For `Object.assign`, each enumerable source property
write is `Set(to, key, value, true)`, not `CreateDataProperty` or
`DefineProperty`.

This distinction is observable:

- an existing accessor property on the target must invoke its setter even when
  the target is non-extensible, sealed, or frozen;
- Symbol keys follow the same write path as string keys;
- failed target writes, such as missing properties on non-extensible targets or
  non-writable data properties, still throw.

Do not "simplify" copy helpers into descriptor creation on the target unless the
ECMAScript algorithm explicitly calls for that operation. Pair this class of
change with focused regressions for accessor targets and failed writes.

## Why

Issue #751 fixed `Array.prototype.at` after the direct array-element path failed
Test262 semantics for sparse holes, inherited indexed properties, and throwing
getters. The durable lesson is not specific to `at`: spec-level `Get` must be
implemented as observable JavaScript property access, and the C# nullable
throw-state check must propagate the JavaScript exception immediately.

Issue #784 / PR #932 fixed strict postfix decrement through a `with` object
environment after the getter deleted the binding before writeback. Issue #785 /
PR #933 confirmed the same binding contract for strict postfix increment. Issue
#786 / PR #975 confirmed prefix decrement must use the same captured binding
writeback path. The generic property setter path could recreate the property,
but ECMAScript strict object-environment `SetMutableBinding` must throw when the
binding has disappeared. The durable lesson is to model object-environment
writeback as a binding operation first and only use property setting after the
strict missing-binding check has passed.

Issue #774 / PR #950 extended that lesson to plain assignment. The RHS can
delete the resolved `with` binding before `PutValue`; strict mode still has to
throw through the captured object-environment reference after RHS side effects,
not fall back to a generic identifier/property assignment path.

Issue #777 confirmed the same object-environment writeback contract for
compound assignment. The compound operator may read through a captured `with`
binding whose getter deletes the property before the final `PutValue`; strict
mode must still re-check the captured object binding and throw `ReferenceError`
instead of letting the generic setter recreate the property per operator.

Issue #829 / PR #1126 fixed simple IR `var` declarations with initializers.
The initializer can delete or otherwise mutate the `with` object after
`ResolveBinding` should already have selected the write target. The durable
lesson is that declaration evaluation has the same observable target-resolution
step: capture the reference before initializer evaluation and write through it
afterward.

Issue #772 / PR #947 fixed object destructuring `var` binding order for a
computed source property under `with`. The durable lesson is that destructuring
binding target resolution is observable and must occur after computed source-key
evaluation but before source getter/default side effects. The runner must keep
that in the compiled binding path and write through captured object-environment
references.

Issue #778 / PR #970 fixed `delete super[expr]` ordering in expression
bytecode. Before `super()` initializes a derived constructor's `this`, the
`super` reference check must throw before the computed property key can run.
After initialization, the key may run, but `delete super[...]` still throws
`ReferenceError`. The durable lesson is to keep super-reference validation,
computed-key evaluation, and the operation-specific throw as separate ordered
steps.

Issue #806 / PR #999 fixed the `Intl.NumberFormat`
`constructor-locales-hasproperty` fixture after `Array.prototype.push` stored
its recursion marker as `__inPush__` on the same JavaScript array used to record
proxy `HasProperty` lookups. The marker polluted later enumeration. The durable
lesson is that native guard state must stay hidden; otherwise guard bookkeeping
becomes a spec-visible property access side effect.

Issue #811 / PR #1007 added focused `Object.assign` regressions after the
issue-supplied Test262 `Object_assign` group was already green but lacked a
local guard for integrity-level accessor targets. The durable lesson is that
`Object.assign` must remain a throwing `Set` operation on the existing target:
integrity-level data-property restrictions do not block an existing setter, and
the same contract applies to Symbol keys.
