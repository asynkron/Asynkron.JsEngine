# Top-level `await` status

Tracking notes for the current top-level `await` bring-up. Use this as a scratchpad while iterating (similar spirit to `docs/fix-assignment-destructuring-evaluation-order.md`).

## What changed this round
- Async dependencies still start evaluation immediately, but async parents are now drained before executing the importing module body so DFS completion ordering and import bindings stay deterministic.
- Blocking waits on async imports detach and restore the caller microtask queue, and microtask enqueue/drain now run under a lock to avoid cross-thread races.

## Current status
- `ModuleCode_topLevelAwait` is green (strict and sloppy).

## Next steps
- Keep an eye on parallel runner behaviour; top-level await still expects deterministic microtask draining while async parents settle.
