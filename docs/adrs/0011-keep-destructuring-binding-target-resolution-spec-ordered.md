# ADR 0011: Keep Destructuring Binding Target Resolution Spec-Ordered

## Status

Accepted

## Context

Issue #772 fixed the Test262 `Destructuring_binding` failure for
`keyed-destructuring-property-reference-target-evaluation-order-with-bindings`.
The failing shape combined a computed object-binding property name, a `var`
binding target inside a `with` environment, source getter access, and a default
initializer.

The compiled binding runner already evaluated object binding properties without
falling back to AST evaluation, but its target-side binding lookup happened too
late for this case. The runtime could evaluate the computed source key and then
reach the source property get or default initializer before preserving the
observable target binding lookup/writeback point. That is wrong for ECMAScript
ordering, and it is especially visible through `with` object environments where
`has`, getter, and default-initializer side effects can all be observed.

The delivery also exposed a separate event-loop ordering hazard: starting the
event loop from a task scheduled during the synchronous top-level evaluation
phase can let host tasks run before the current JavaScript evaluation has
finished its synchronous prefix.

Issue #1070 / PR #1235 extended the same compiled binding surface to
`Statements_variable_dstr` crashes. Generic array binding targets could raise a
`ThrowSignal` while applying nested/default binding programs, but
`HandleBindingVariableDeclaration` let that host signal escape instead of
normalizing it into the active `EvaluationContext`. Negative Test262 fixtures
then saw an unhandled runtime crash where ECMAScript expected a JavaScript
throw completion.

Issue #1063 / PR #1303 closed out `Statements_class_dstr` by adding class
method destructuring regressions. Review caught that the abrupt-completion
iterator-close regression was too weak when it only observed iterator
`return()` side effects: a default initializer that throws during class-method
parameter binding must both close the active iterator exactly once and preserve
the original JavaScript throw for the caller.

## Decision

Compiled object destructuring binding must keep the spec ordering explicit:

1. evaluate the computed source property name;
2. resolve or capture the binding target side effect for the binding mode;
3. read the source property;
4. evaluate the default initializer only when needed;
5. write through the captured binding target when one was resolved.

For `var` binding in a `with` object environment, the runner should capture the
object-environment binding reference at the target-resolution point and use that
captured reference for the later write. It must not re-probe or fall through to
generic identifier assignment after source getter/default side effects have run.

Keep this behavior in the compiled binding/IR runner surface. Do not add an AST
evaluation fallback to repair this ordering.

When a compiled binding-target program can produce a JavaScript throw during a
declaration, the declaration instruction owns conversion back to
`EvaluationContext` before ordinary IR abrupt-completion handling runs. Do not
let internal `ThrowSignal` values escape directly from
`ApplyBindingTargetProgram`; route them through the same declaration throw path
that preserves `try`/`catch`, iterator close, and expected Test262 negative-case
semantics.

Class method parameter destructuring uses the same binding-target semantics for
defaults and iterator cleanup. If a default initializer abruptly completes while
an array pattern is consuming an iterator, keep the original throw as the
observable completion and run `IteratorClose` once for the active iterator.

Tasks scheduled while `JsEngine.Evaluate` is still executing its synchronous
script/module prefix should be deferred until the prefix completes. Flushing the
deferred queue after synchronous evaluation preserves top-level JavaScript
ordering while still allowing the event loop to drain pending work afterward.

## Consequences

- Future destructuring binding changes must treat target binding lookup as an
  observable step, not as an implementation detail that can be moved after
  source property access or defaults.
- Assignment destructuring and binding declarations still have different target
  timing rules; do not unify them unless focused proof covers both sides.
- `with` object-environment writeback must use captured binding references for
  this path, including strict/sloppy missing-binding behavior.
- Generic binding target runners must normalize JavaScript throws into the
  active evaluation context at declaration boundaries. A passing narrow
  negative Test262 fixture is not enough if the host `ThrowSignal` can bypass
  `HandleBindingVariableDeclarationThrowSlow`.
- Class-method destructuring regressions for abrupt defaults must assert both
  sides of the obligation: the thrown JavaScript error still reaches user code,
  and the iterator's `return()` hook ran exactly once.
- Event-loop work scheduled during synchronous top-level evaluation must not run
  until the synchronous prefix has completed.
- Regression proof should include an internal ordering test with computed source
  key, `with` binding lookup, source getter, and default initializer, plus the
  focused `Name=Destructuring_binding` Test262 group. For declaration binding
  throw propagation, prove the focused `Name=Statements_variable_dstr` Test262
  group.
