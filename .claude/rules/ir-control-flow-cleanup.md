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
4. Add narrow tests for both sides of this class of bug: bindings from exited
   lexical scopes must not leak, and cleanup-wrapped same-loop continues must
   not run iterator close/finally as if they exited the loop.

## Why

Issue #756 / PR #889 fixed `BlockScope_leave` after labeled breaks could bypass
block `PopEnvironment` cleanup and leak `let` bindings. The build-back follow-up
then fixed a same-loop `continue` wrapped by cleanup instructions: the for-of
try/finally skip check had to resolve through the cleanup chain before comparing
with the loop continue target. Treat abrupt control-flow cleanup as part of the
IR contract, not as an incidental jump target detail.
