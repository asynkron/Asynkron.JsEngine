# ADR 0085: Keep yield star delegated return completion split

## Status

Accepted

## Context

Issue #1039 / PR #1278 fixed the focused Test262 `Expressions_yield` crash
group after the internal quality gate exposed a delegated `yield*` return
regression.

The failing path involved an outer generator returning through `yield*` while
the delegated iterator's `return()` result was awaited. For non-generator
iterators, an awaited `return()` result must not immediately complete the outer
delegation just because the return path is active. The iterator can continue
delegating on the next resume, and premature completion loses the delegated
value flow.

At the same time, generator delegates need a different rule. A generator
delegate can yield cleanup values from `finally` during return propagation
(`done: false`) while still carrying a pending return completion. The runner
must preserve that pending completion and replay it across resumes until the
delegated generator completes.

## Decision

Keep `yield*` delegated return completion state split by delegated iterator
shape and return result timing:

- generator delegates stay in delegated return-completion mode while a
  propagated return is pending, including temporary `done: false` cleanup
  yields;
- non-generator iterators enter delegated return-completion mode only when
  `return()` synchronously reports `done: true`;
- awaited non-generator `return()` results must resume delegation instead of
  forcing immediate outer completion;
- ordinary `throw()` results that complete normally are handled as iterator
  results, not as throw propagation.

Do not collapse these cases into a single `propagateReturn` or
`propagateThrow` flag. The pending abrupt-completion state is part of the
observable `yield*` protocol, not just runner bookkeeping.

## Consequences

- Future changes to `DelegatedYieldState` or the generator handlers must keep
  delegated generator cleanup yields separate from non-generator awaited
  `return()` continuation.
- Regression coverage for this area needs both sides: generator-delegate
  return through cleanup `done: false`, and non-generator awaited `return()`
  continuation.
- Focused proof should include the local generator regressions and the owning
  Test262 method group, for this issue: `Name=Expressions_yield`.
- This ADR is caused by issue #1039 / PR #1278 and is enforced by
  `.claude/rules/ir-control-flow-cleanup.md`.
