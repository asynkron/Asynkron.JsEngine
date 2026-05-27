# ADR 0208: Keep unified bytecode branch production routing shape-discriminated

## Status

Accepted

## Context

Issue #2227 / PR #2239 widened unified bytecode sync production routing beyond
the neutral slot/literal/store/return subset from ADR 0204 by admitting one
control-flow opcode: a direct forward `JumpIfFalse` branch-return program.

The friction point was not VM execution of truthiness itself. The VM already
executes `JumpIfFalse` through `IsTruthy`, and the prototype compiler already
emits branch bytecode. The production risk was the routing boundary. An early
delivery step temporarily moved unified bytecode ahead of
`SyncIrCallTrampoline` so a direct parameter branch like
`if (flag) return 1; return 2;` logged `unified-bytecode-production-fast-path`.
Review correctly restored the ADR 0204 ordering: existing simple-return
shortcuts and the trampoline stay ahead of unified bytecode. That made the
first invocation proof conflict with AC-3 because the direct parameter branch
was still owned by the higher-priority trampoline route.

The final delivery kept the ordering intact and changed the proof shape to use
a local selector:

```javascript
function pick(flag) {
    var branch = flag;
    if (branch) {
        return 1;
    }

    return 2;
}
```

That source lowers to the same direct `JumpIfFalse` branch-return production
program shape while avoiding the existing trampoline shortcut. The accepted
route can therefore prove production unified bytecode invocation without
shadowing stronger sync-call fast paths.

## Decision

Keep direct branch production routing shape-discriminated and fast-path ordered.

- Accept `JumpIfFalse` in production only for a single direct forward
  branch-return program: exactly one `JumpIfFalse`, no `Jump`, a forward false
  target, and both branch arms as immediate `LoadSlot` or `LoadLiteral`
  followed by `Return`.
- Keep every other `JumpIfFalse` topology prototype-only for production,
  including nested branches, joins, loops, and other control-flow families.
- Keep `Jump` prototype-only for production so branch joins and loop back-edges
  cannot enter the route through this slice.
- Keep `Binary` declined before structural control-flow declines. Branch or
  loop routing must not become a back door for unproven operator coercion or
  abrupt-completion semantics.
- Preserve ADR 0204 sync fast-path order. Unified bytecode production
  invocation remains behind direct simple-return numeric/binary shortcuts and
  `SyncIrCallTrampoline`, and ahead of the generic simple IR activation runner.
- When proving a production route for a shape family that overlaps an earlier
  fast path, use an invocation test shape that distinguishes the desired route
  without reordering the invoker. If the desired source shape is intentionally
  supposed to preempt an existing route, make that priority change explicit and
  prove the older route remains covered.
- Keep evidence paired: selector acceptance, adjacent declines, true/false
  invocation outcomes, production-route logging for the selected shape, and a
  negative proof that existing specialized fast paths still win.

## Consequences

- Unified bytecode now has a production control-flow foothold for direct
  truthiness branch-return functions without admitting branch joins, loops, or
  Binary conditions.
- Future production widening must update the selector and invocation proof
  together. A selector-only acceptance is not enough if a higher-priority route
  owns the same source shape at runtime.
- Existing sync-call fast paths remain stable. Production unified bytecode can
  expand by choosing non-overlapping proof shapes or by making deliberate,
  separately reviewed priority changes.
- Evidence docs and tests should name the actual route being exercised so a
  restored fast-path ordering cannot leave stale claims about production logs.

## Related

- Issue #2227
- PR #2239
- Commit `4429d144417168983ffb7a8b2825e735e239c6d9`
- ADR 0201: `docs/adrs/0201-keep-unified-bytecode-production-routing-decline-first.md`
- ADR 0204: `docs/adrs/0204-keep-unified-bytecode-sync-production-routing-slot-bridged.md`
- ADR 0205: `docs/adrs/0205-keep-unified-bytecode-binary-production-eligibility-operator-explicit.md`
- ADR 0192: `docs/adrs/0192-keep-unified-bytecode-acyclic-control-flow-compiler-owned.md`
- `.claude/rules/unified-bytecode-prototypes.md`
- `docs/performance/unified-bytecode-branch-production-routing.md`
- `src/Asynkron.JsEngine/Execution/UnifiedBytecode/UnifiedBytecodeProductionEligibility.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionEligibilityTests.cs`
- `tests/Asynkron.JsEngine.Tests/UnifiedBytecodeProductionInvocationTests.cs`
