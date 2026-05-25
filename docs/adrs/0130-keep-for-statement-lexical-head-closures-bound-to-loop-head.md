# ADR 0130: Keep for statement lexical head closures bound to loop head

## Status

Accepted

## Context

Issue #1838 / PR #1863 closed the Test262
`language/statements/for/scope-head-lex-close.js` crash slice. The issue named
a classic `for` statement shape where a `let` binding in the loop head shadows
an outer binding, and closures are created from all three loop regions:
condition, increment, and body.

The carried build evidence showed that current `origin/main` already passed the
focused Test262 row:

```text
rtk dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release \
  --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings \
  --filter "Name~scope-head-lex-close"
```

The delivery therefore stayed test-only. It added an internal regression that
mirrors the fixture and asserts the observable contract: closures from the
condition, increment, and body all see the loop-head `inside` binding, while
the outer `x` binding is restored to `outside` after loop completion.

## Decision

Classic `for (let ...)` execution must keep the loop-head lexical environment
as the binding captured by closures created from the test expression,
increment expression, and body. Exiting the loop must restore the outer
environment so the shadowed outer binding remains observable after the loop.

Do not repair a `scope-head-lex-close` style failure by flattening the
loop-head binding into the outer scope, by sharing the outer binding with head
closures, or by treating the condition/increment expressions as if they were
outside the loop-head lexical environment.

When a stale Test262 crash issue for this shape is green on current main, a
focused internal regression is the preferred closeout. Runtime or harness code
should change only after a current failing fixture proves that the loop-head
environment contract is broken.

## Consequences

- Future classic `for` scope changes must test closures created in the
  condition, increment, and body together, plus the restored outer binding after
  loop exit.
- Loop-scope cleanup work should distinguish "restore the outer environment"
  from "make loop-head closures use the outer environment"; both properties are
  observable and required.
- Test262 crash reports for this fixture should start with the exact
  `scope-head-lex-close` row before touching loop lowering, environment reuse,
  or harness behavior.
