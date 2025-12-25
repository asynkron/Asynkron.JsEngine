# TODO Index

## Task Files

| Status | Task | File |
|--------|------|------|
| ✅ | Nested For-Await-Of Loop Bug | [todo-nested-async-loop-bug.md](todo-nested-async-loop-bug.md) |
| ✅ | Async For-Of Optimization | [todo-async-for-of.md](todo-async-for-of.md) |
| ⬜ | Universal IR Execution | [todo-universal-ir.md](todo-universal-ir.md) |

## Legend

- ✅ Done
- ⬜ In Progress / Not Done

## Quick Status

### Nested For-Await-Of Loop Bug ✅
**FIXED** - When `for await...of` is nested inside a regular `for` loop, the inner loop now correctly executes on all outer iterations. Fix walks up environment chain to find correct slot scope.

### Async For-Of Optimization ✅
**DONE** - Phase 1 completed: eliminated dictionary lookups by storing JsVariables on IteratorDriverState. Phases 2-4 intentionally skipped/deferred (backward compat, ES spec requirements, future work).

### Universal IR Execution ⬜
Analysis document for using IR generator for all JavaScript functions. Documents current state and remaining work needed.
