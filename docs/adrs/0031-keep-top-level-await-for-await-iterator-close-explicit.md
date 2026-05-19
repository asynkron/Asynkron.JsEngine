# ADR 0031: Keep top-level await for-await iterator close explicit

## Status

Accepted

## Context

Issue #805 / PR #997 fixed the Test262
`ModuleCode_topLevelAwait_syntax` failures for top-level-await module syntax.
The delivery repaired several async module body shapes in `JsEngine`, including
exported class declarations with awaited class syntax, catch binding defaults,
and `for await (... of await iterable)` bodies.

The review-back failure was in the top-level-await bridge for
`for await (... of await iterable) { await ...; break; }`. That bridge used a
narrow single-iteration shortcut so the async module body could resume after an
awaited iterable and awaited body expression. The shortcut advanced the module
body after `break` without invoking and awaiting the async iterator's
`return()` method. A real async generator could therefore skip its `finally`
block, even though ECMAScript requires async iterator close on abrupt loop
completion.

This is distinct from the broader TLA scheduling rule in ADR 0027. Scheduling
keeps module continuations in the right microtask order; iterator close keeps
the observable cleanup semantics of the loop frame intact before the module
body continues.

## Decision

Top-level-await bridge paths for `for await` must preserve async iterator close
semantics before resuming module evaluation.

If a bridge handles an awaited iterable or awaited loop body outside the normal
IR iterator driver, it owns the same abrupt-completion obligations as the
normal loop runtime. A `break`, `return`, thrown completion, or other loop exit
must call the active async iterator's `return()` method when present, await the
returned promise/value, and only then advance the module body continuation.

Do not treat a single-iteration bridge as a statement shortcut that can skip
`AsyncIteratorClose`. The shortcut is only valid when it preserves both the
await scheduling and the loop cleanup contract.

## Consequences

- Future TLA `for await` fixes must inspect both continuation scheduling and
  iterator cleanup before claiming the bridge is equivalent to the normal loop
  path.
- Regression coverage for this class needs a real async generator with a
  `finally` block or observable `return()` hook, not only final loop values.
- The focused proof should keep the local TLA `for await` regression alongside
  the Test262 `Name=ModuleCode_topLevelAwait_syntax` method group.
- If the bridge grows beyond the current narrow shapes, prefer sharing the
  normal iterator-close helper or lowering to the normal iterator driver rather
  than adding another AST/bridge-only cleanup path.
