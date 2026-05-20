# ECMAScript Modules

When changing module import/export resolution or top-level-await scheduling,
keep local bindings, indirect re-export bindings, direct namespace export
values, and async module evaluation order separate.

## Rules

1. Create module environment bindings only for local declarations and local
   export aliases. A re-export with `FromModule` is not a local lexical name in
   the exporting module.
2. Model `export * as ns from "module"` as an export entry for the source
   module namespace object. Do not define `ns` in the exporting module
   environment.
3. Let export resolution represent both environment-backed bindings and direct
   export values. Import binding creation, live export creation, and star
   ambiguity checks must handle direct values explicitly instead of forcing them
   through synthetic local bindings.
4. Preserve ordinary `export * from` default exclusion separately from explicit
   namespace export forms. Do not use one export-star path as a shortcut for the
   other without focused Test262 proof.
5. Prove namespace re-export fixes with both consumer and exporter visibility:
   importing `{ ns }` from the barrel must work, while `typeof ns` inside the
   barrel remains `undefined`.
6. For top-level-await modules, do not force-drain the whole microtask queue
   during import binding resolution. If an async import must complete before its
   binding can be read, preserve unrelated pending microtasks around that wait.
7. Treat already-settled top-level-await operands as async module continuations:
   schedule fulfillment through the engine microtask queue instead of invoking
   the continuation synchronously from the module body runner.
8. Preserve self-import cycles as the current module. Do not eagerly re-evaluate
   the current module through a self-import edge, and keep unresolved default
   imports observable as JavaScript TDZ behavior until the export initializes.
9. When a top-level-await bridge handles `for await` outside the normal loop
   runner, preserve `AsyncIteratorClose`: abrupt loop completion must invoke
   and await the active async iterator's `return()` before the module body
   continuation advances.
10. Keep TLA proof packs focused on scheduling and cleanup, not just final
   values: sibling async modules, import tick ordering, self-import tick
   ordering, real async-generator `finally`/`return()` cleanup, and
   `Name=ModuleCode_topLevelAwait`.
11. Treat the engine microtask queue as a serialized runtime boundary when async
    module continuations can resume on different managed threads. Protect queue
    enqueue, detach, prepend, dequeue, deferred requeue, and drain-state updates
    together, but never execute microtask callbacks while holding that lock.
12. In top-level-await `try/catch` runners, route class declarations with
    awaited computed method or accessor names through the shared
    class-declaration await-temp path. Do not add a generic AST fallback or a
    try-runner-local computed-name evaluator for this shape.

## Why

Issue #803 / PR #992 fixed the Test262 `ModuleCode` fixture
`language/module-code/export-star-as-dflt.js`. The old runtime made
`export * as ns from "values.js"` create a local `ns` binding in the exporting
module, which let code inside the barrel observe a name that ECMAScript treats
as export-only. The repair stores the namespace object as a direct export value
and teaches import/export resolution to consume that value without inventing a
source lexical binding.

Issue #804 / PR #1006 fixed Test262 `ModuleCode_topLevelAwait` scheduling
failures. The durable trap was that import-time dependency completion, already
settled awaits, sibling async modules, and self-import cycles share the same
module runtime surface but have different microtask-ordering requirements.
Future repairs must preserve the observable ticks instead of using broad
microtask drains or synchronous continuation calls to make binding reads pass.

Issue #805 / PR #997 fixed Test262 `ModuleCode_topLevelAwait_syntax` `for
await (... of await iterable)` bridge behavior. The trap was that a
single-iteration top-level-await shortcut still owns the loop cleanup contract:
breaking out of the loop must call and await the async iterator's `return()` so
async generator `finally` blocks run before the module body resumes.

Issue #1032 / PR #1106 fixed an async module continuation race in `JsEngine`.
The durable lesson is that JavaScript execution can remain logically
single-threaded while host continuations still mutate the shared microtask
queue from different managed threads. Future TLA repairs must preserve ADR
0027's tick ordering and serialize the queue structure, not replace the race
with synchronous continuations or broad forced drains.

Issue #825 / PR #1120 fixed Test262 `Statements_class` failures for
top-level-await modules where a `try` block contained a class declaration with
awaited computed method or accessor names. The durable lesson is that
specialized TLA statement bridges must reuse the syntax owner's await-temp path:
class declaration evaluation owns computed names, accessors, static elements,
binding initialization, and resumption as one operation.
