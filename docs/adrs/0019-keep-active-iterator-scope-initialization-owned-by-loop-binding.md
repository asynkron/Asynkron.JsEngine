# ADR 0019: Keep active iterator scope initialization owned by loop binding

## Status

Accepted

## Context

Issue #791 / PR #984 fixed the Test262 `Iterator_prototype_filter` failures
for `built-ins/Iterator/prototype/filter/predicate-filters.js`.

The visible failure looked like an `Iterator.prototype.filter` helper bug, but
the root cause was in the IR runner's `for-of` scope setup. On the first active
iterator iteration, the iterator driver had already established the loop scope
that the loop binding statement would initialize. The per-iteration environment
creation path still tried to copy same-name per-iteration bindings from the
enclosing loop scope before that binding had been initialized.

For a shape such as:

```javascript
for (let value of [1, 2, 3]) {
  values.push(value);
}
let { value, done } = { value: 4, done: true };
```

that copied the outer `value` TDZ state into the first iteration environment
instead of letting the loop binding initialize the active iteration binding.
The downstream Test262 fixture only exposed the symptom through iterator helper
predicate filtering.

## Decision

The active iterator iteration environment is owned by the iterator driver's
loop binding initialization, not by the generic per-iteration copy path.

When `IteratorDriverState.LoopScopeEnvironment` is the same environment that is
being used as the loop scope for a new iteration, first-iteration setup must not
copy same-name per-iteration bindings from the enclosing loop scope. The loop
binding statement owns initialization for that active iterator frame.

Subsequent iterations still copy per-iteration bindings from the previous
iteration environment. Non-iterator and nested loop-scope setup keeps using the
normal per-iteration copy behavior.

## Consequences

- Future `for-of` scope changes must identify which component owns a
  binding's initialization before copying TDZ state across environments.
- Test262 failures in iterator helper groups can still originate in shared
  loop-scope execution semantics. Reduce them to a local loop-binding repro
  before changing helper-specific code.
- Focused proof should include both the local loop-binding shape and the owning
  Test262 method group or fixture that exposed it.
