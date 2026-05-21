# ADR 0089: Keep suspended array-pattern iterators resumable and closable

## Status

Accepted

## Context

Issue #1339 / PR #1353 fixed a mixed Test262 crash bucket across iterator
helpers, async generators, and destructuring lifecycle cases. The delivery
repaired array binding destructuring in generator contexts and the normal
`IteratorClose` validation path exposed by `Iterator.prototype.take`.

The key failure was not just that array destructuring needed to close an
iterator on abrupt completion. That already existed for ordinary completion
paths. The missing state was the active iterator that lives across a generator
or async generator suspension while a default initializer awaits or yields.
Closing it immediately on suspension breaks resumability, but losing it means a
later generator `return()` cannot run the iterator's `return()` cleanup.

The implementation therefore added an `EvaluationContext.InGeneratorContext`
marker, registers an active array-pattern iterator state in the function
environment only while the pattern is suspended, and removes or marks that
state closed when iteration completes or explicit close runs.

## Decision

Array binding pattern execution in generator contexts must keep suspended
iterators as resumable state, not as ordinary abrupt-completion cleanup.

When array destructuring can suspend while an iterator is active:

- do not call `IteratorClose` merely because evaluation stops for `yield` or
  pending `await`;
- do expose the active iterator through environment state so generator
  `return()` can close it later;
- delete that environment state once the pattern resumes past suspension or the
  iterator is fully consumed/closed;
- mark the state closed whenever normal completion or explicit iterator close
  has already handled cleanup.

For normal `IteratorClose`, the `return()` result must still be validated as an
object. Iterator helper delivery paths should use the same observable close
contract rather than treating a host return call as fire-and-forget cleanup.

## Consequences

- Future destructuring, generator, and iterator-helper fixes must distinguish
  suspension from abrupt completion before closing iterators.
- Generator `return()` cleanup must be able to discover suspended array-pattern
  iterators without adding helper-specific fallback paths.
- Regression coverage should include a generator or async generator default
  initializer that suspends while an array destructuring iterator remains
  active, plus a normal `IteratorClose` result-shape check.
- Focused proof for this issue used representative Test262 filters covering
  async generator destructuring, ordinary function/for-of destructuring, and
  `Iterator.prototype.take/return-is-forwarded`.
