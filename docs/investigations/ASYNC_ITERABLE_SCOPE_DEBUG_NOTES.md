# Async Iterable Scope Debug Notes

## Overview

`AsyncIterableScopeComparisonTests.CompareGlobalVsLocalScope_WithDebug` now materializes the raw `DebugMessage` entries into structured snapshots so we can compare the environments observed during the local and global executions. The test fails whenever the global run omits iterator scaffolding that the local run provides. This document captures the current discrepancy so we have a single source of truth while the runtime bug is being investigated.

## Snapshot Summary

| Scenario | Snapshot Count | Iterator Temporaries (`__iterator*`) | Loop Items (`item`) | Notes |
|----------|----------------|--------------------------------------|---------------------|-------|
| Local    | 5              | Present from snapshot 1 onward       | `x`, `y`, `z`       | Loop executes normally and accumulates `result = "xyz"`. |
| Global   | 1              | **Missing**                          | **Missing**         | Only the pre-loop snapshot is emitted; the loop body never runs. |

The absence of iterator temporaries and loop variables in the global run indicates that the CPS async machinery never advances the iterator after it is retrieved.

## Failure Message

When the environments diverge the assertion message from the test reads:

```
Global execution never exposed iterator temporaries (prefix '__iterator').
Global execution never surfaced 'item' loop variables, indicating the for-await-of body did not run.
See docs/investigations/ASYNC_ITERABLE_SCOPE_DEBUG_NOTES.md for the captured environment diff.
```

These lines should disappear once the engine keeps the iterator alive in global scope.

## Next Steps

1. Instrument the iterator helpers (`__getAsyncIterator`, `__iteratorNext`) to confirm the returned iterator instance is retained across awaits when defined on the global object.
2. Inspect the CPS lowering for global bindings to ensure closures capture the binding instead of copying the value at transformation time.
3. Update this document with new observations whenever the failing test surfaces different parity issues.

## Reproducing the Issue

Run the single test:

```bash
dotnet test --filter "FullyQualifiedName~AsyncIterableScopeComparisonTests.CompareGlobalVsLocalScope_WithDebug"
```

Inspect the output for the snapshot summaries to confirm the missing iterator state.
