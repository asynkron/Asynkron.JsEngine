# ADR 0027: Keep top-level await module scheduling microtask-ordered

## Status

Accepted

## Context

Issue #804 / PR #1006 fixed Test262 `ModuleCode_topLevelAwait` failures in
the ES module runtime. The failing cases were not about parsing top-level
`await`; they were about when async module dependencies, sibling modules,
self-imports, and already-settled awaits are allowed to complete.

The delivery touched the shared module owner surface in `JsEngine`:

- module registry reuse and self-import cycle handling
- async dependency draining and DFS parent completion order
- import-time binding completion for default, named, and namespace imports
- microtask queue preservation while resolving async imports
- top-level-await continuation scheduling for already-settled values

It also had an adjacent cleanup effect: iterator cleanup scans can encounter
unresolved import bindings during self-import cycles. Those TDZ imports are not
iterator state and must not leak a host-side `ReferenceError` outside JavaScript
`try/catch`.

## Decision

Top-level-await module evaluation must preserve ECMAScript async module
scheduling instead of treating import resolution as permission to drain all
microtasks immediately.

When an import requires the imported module to complete before a binding can be
read, the runtime may await that module completion, but it must detach and
restore unrelated pending microtasks around that wait. Import-time forced
draining must not collapse the tick boundary that Test262 observes between
synchronous module evaluation, async dependency settlement, and the importing
module's own `await`.

Already-settled top-level-await operands are still asynchronous from the
module-evaluation perspective. Their fulfillment continuations must be queued
through the engine microtask queue rather than invoked synchronously.

Self-import cycles are handled as the current module, not as a dependency to
eagerly evaluate. A self-import can expose an unresolved default binding while
the module body is still running; that unresolved binding remains a JavaScript
TDZ condition until the exported value is initialized.

Async dependency draining remains deliberate. Adjacent async dependencies may
be drained together to preserve DFS async parent completion order, but that is
different from globally draining import-time microtasks.

## Consequences

- Future top-level-await fixes must inspect module registry reuse,
  `EvaluateImport`, dependency draining, and await continuation scheduling
  together.
- Do not repair a TLA ordering failure by adding a broad
  `DrainMicrotasks(force: true)` in import resolution.
- Do not synchronously invoke settled await continuations from the module body
  runner; schedule them as microtasks.
- Do not eagerly evaluate the current module through a self-import edge.
- TDZ-safe environment reads are valid only for cleanup/introspection paths that
  are explicitly looking for owned runtime state, such as active iterators.
  They must not become a general way to hide JavaScript binding errors.
- The focused proof for this class should include local module regressions for
  sibling async modules, async import tick ordering, and self-import tick
  ordering, plus the Test262 `Name=ModuleCode_topLevelAwait` method group.
