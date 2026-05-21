# IR Control-Flow Cleanup

When changing IR emission or execution for `break`, `continue`, labeled
statements, loops, lexical blocks, or `try`/`finally`, preserve emitted lexical
scope cleanup chains.

## Rules

1. If abrupt control flow crosses a lexical scope, route the target through the
   scope's emitted `PopEnvironmentInstruction` chain. Do not jump directly to
   the final label, loop, or block exit.
2. Do not replace emitted cleanup chains with a runtime-only environment rewind
   unless the change proves equivalent `PopEnvironment` behavior, including
   disposal and pooling metadata.
3. When classifying a target at runtime, such as checking whether a `for-of`
   `continue` stays inside the same loop, follow leading
   `PopEnvironmentInstruction` nodes before comparing against loop targets.
4. For `for-of` `break` handling, preserve both loop continue and loop break
   target metadata. A break to a label inside the current loop body can skip the
   current loop's iterator close path, but a break to an outer label is still an
   exit from the current iterator frame and must schedule finally/IteratorClose.
5. Add narrow tests for both sides of this class of bug: bindings from exited
   lexical scopes must not leak, and cleanup-wrapped same-loop continues must
   not run iterator close/finally as if they exited the loop. For nested
   `for-of`, also test that a labeled break to an outer loop calls the inner
   iterator's `return()` exactly once.
6. For active `for-of` iterator iterations, do not copy same-name
   per-iteration bindings from the enclosing loop scope when the iterator
   driver's `LoopScopeEnvironment` is the loop scope being used for the new
   iteration. The loop binding statement owns first-iteration initialization;
   copying from the enclosing scope can preserve TDZ state before the binding is
   initialized.
7. For `try`/`finally` IR completion bookkeeping, keep pending abrupt
   completion origin explicit. A normal `finally` must restore the saved
   try/catch completion value for pending `break` or `continue` that originated
   before the finally block; an abrupt `break`, `continue`, `return`, or `throw`
   raised inside `finally` must still replace the saved completion.
8. If an abrupt target can be registered while a `with` object environment is
   active, store the full current scope-exit boundary, not only an integer
   lexical scope id. Dynamic `with` frames have to be matched by their
   slot/frame identity so cleanup stops at the enclosing object environment
   instead of treating the no-lexical-scope case as unbounded cleanup.
9. For `yield*` delegated return handling, keep generator delegates and
   non-generator iterators split. Generator delegates must preserve pending
   return completion across temporary cleanup yields (`done:false`), while
   non-generator iterators only enter delegated return completion when
   `return()` synchronously reports `done:true`. Awaited non-generator
   `return()` results must resume delegation instead of immediately completing
   the outer generator.

## Why

Issue #756 / PR #889 fixed `BlockScope_leave` after labeled breaks could bypass
block `PopEnvironment` cleanup and leak `let` bindings. The build-back follow-up
then fixed a same-loop `continue` wrapped by cleanup instructions: the for-of
try/finally skip check had to resolve through the cleanup chain before comparing
with the loop continue target. Treat abrupt control-flow cleanup as part of the
IR contract, not as an incidental jump target detail.

Issue #790 / PR #968 added the break-side guardrail: a labeled break out of an
outer loop from inside an inner `for-of` must close the inner iterator. That
failure showed that "labeled break" is not enough information; future changes
must classify whether the target stays inside the current loop frame or exits it.

Issue #791 / PR #984 fixed Test262 `Iterator_prototype_filter` failures whose
symptom looked helper-specific but whose cause was active iterator scope setup.
The first `for-of` iteration copied a same-name TDZ binding from the enclosing
scope before the loop binding statement initialized the iteration binding. Keep
iterator-frame ownership explicit before adding helper-specific workarounds.

Issue #828 / PR #1127 fixed Test262 `Statements_try` completion-value failures
after review exposed a try/finally pairing bug. The first repair let abrupt
completion from inside `finally` win, but it also risked overwriting a try-body
`break` or `continue` that merely passed through a normal `finally`. The
durable rule is that `EndFinally` needs origin-aware pending completion state:
normal finally completion discards its own value and restores the saved
try/catch completion, while abrupt finally completion replaces it.

Issue #830 / PR #1131 fixed Test262 `Statements_with` failures after `break`
cleanup inside loops and switches nested in an enclosing `with` frame could pop
the dynamic object environment too far. The durable rule is that abrupt cleanup
boundaries are not lexical ids only: when `with` is active, loop, switch, and
labeled target emitters must capture a boundary that can identify the dynamic
frame by slot identity and prove property lookup after the break still sees the
enclosing object.

Issue #1039 / PR #1278 fixed Test262 `Expressions_yield` after delegated
`yield*` return handling collapsed different iterator-return shapes. Generator
delegates can yield cleanup values while a return completion is still pending,
but non-generator iterators with awaited `return()` results must keep
delegating on resume unless `return()` synchronously completed with
`done:true`. The durable rule is that delegated abrupt-completion state must
preserve that split instead of treating every propagated return or throw as the
same pending completion.
