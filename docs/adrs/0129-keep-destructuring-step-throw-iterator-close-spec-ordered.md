# ADR 0129: Keep destructuring step-throw iterator close spec-ordered

## Status

Accepted

## Context

Issue #1837 / PR #1860 fixed the Test262 destructuring iterator error/close
crash slice. The delivery repaired execution-plan destructuring paths where an
iterator `next()` call could throw while array binding destructuring was still
active.

The existing runner paths already handled several abrupt-completion surfaces,
but `IteratorDriverState.Next(context)` can report failure in two equivalent
runtime shapes: by setting the evaluation context throw state, or by throwing a
`ThrowSignal`. The `ThrowSignal` path bypassed the normal destructuring close
logic, so a `for (const [x] = iterable; ; )` initializer whose iterator
`next()` threw did not reliably call the iterator's `return()` before routing
the original throw through execution-plan try/catch handling.

The fix made destructuring step failure normalize both runtime shapes into the
same close-and-route path: capture the thrown value, clear transient context
state, call `IteratorClose` when the iterator is active and not done, let an
abrupt close replace the throw only when the close itself reports a throw, then
dispose the iterator driver and route the final throw through
`HandleAbruptCompletion`.

## Decision

Execution-plan destructuring must treat iterator step failure as ordinary
destructuring abrupt completion, regardless of whether the failure arrives via
`EvaluationContext` throw state or a caught `ThrowSignal`.

When an array binding pattern owns an active iterator and `next()` fails:

- capture the original thrown value before clearing the context;
- run `IteratorClose(context, preserveExistingThrow: true)` if the iterator is
  active and not done;
- preserve the original thrown value unless `IteratorClose` itself produces a
  throw;
- dispose the iterator driver before returning or rethrowing;
- route the resulting throw through execution-plan abrupt-completion handling
  so enclosing `try`/`catch` observes the correct value.

Do not special-case a `ThrowSignal` step failure as a direct rethrow that skips
destructuring cleanup, and do not treat `context.ShouldStopEvaluation` after a
binding-target operation as proof that the iterator can be abandoned.

## Consequences

- Future destructuring runner changes must keep context-throw and
  `ThrowSignal` paths semantically paired.
- Iterator close checks for array binding patterns should include a `next()`
  throw shape, not only binding-target/default-initializer throws.
- Tests should prove both observable requirements together: `return()` is
  called on the active iterator and the caught value remains the original step
  throw unless the close path throws.
- This belongs with the broader IR abrupt-completion cleanup contract rather
  than helper-specific iterator code; the failure was in runner-level
  completion routing.
