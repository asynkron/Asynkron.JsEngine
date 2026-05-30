# Closure Simple IR Activation Fast Path

## Summary

Removed an overly conservative `_argumentsObjectNeeded` guard from
`CanUseSimpleIrActivationFastPath`, allowing closure functions (and other
non-arrow functions that don't actually use `arguments`) to take the simple IR
activation fast path instead of the full `ExecutionPlanRunner` path with struct
boxing.

## Baseline signal

Baseline signal: `activation-closures-lite` = Jint 5.59x faster (Asynkron 1807ms, Jint 323ms)

## Final signal

Final signal: `activation-closures-lite` = Asynkron 1.13x faster (Asynkron 267ms, Jint 301ms)

Signal delta: Asynkron time 1807ms → 267ms = **6.77x speedup** (−85% execution time)

Additional improvements from same fix:
- `activation-arguments-lite`: 2354ms → 603ms (3.90x speedup)
- `activation-evalscope-lite`: 2686ms → 516ms (5.20x speedup)

## Root cause

The CPU profiler showed `CastHelpers.Box` consuming **85.7%** of
`InvokeWithContextSlow` time for the `activation-closures-lite` benchmark.

The inner closure function (`function inner(step) { base += step; return base; }`)
had `_argumentsObjectNeeded = true` because it is not an arrow function and
`arguments` is neither a parameter name nor declared in its body. This flag blocked
`CanUseSimpleIrActivationFastPath`, causing `TryInvokeIrFast` to return false.

As a result, `InvokeWithContextSlow` fell through to the full path:
```csharp
var runner = new ExecutionPlanRunner(_function, _closure, arguments, ...);
```

Here `arguments` is a `SingleValueArgs` struct (passed from `InvokeWithContext1`).
Assigning it to `IReadOnlyList<JsValue> _arguments` in `ExecutionPlanRunner` caused
a heap allocation (boxing) on every call — 120,000 times per benchmark iteration.

## Fix

In `CanUseSimpleIrActivationFastPath`, removed `_argumentsObjectNeeded ||` from
the guard condition. The remaining checks `_usesArguments ||` and
`_needsArgumentsBinding ||` already cover all cases where the `arguments` object
is actually accessed by the IR plan. When both are false, no IR instruction reads
the `arguments` binding, so skipping its creation is safe.

```csharp
// Before
_argumentsObjectNeeded ||
_usesArguments ||
_needsArgumentsBinding ||

// After
// _argumentsObjectNeeded removed — safe when _usesArguments && _needsArgumentsBinding are false
_usesArguments ||
_needsArgumentsBinding ||
```

The simple IR activation fast path creates the execution environment via
`CreateSimpleIrActivationEnvironment`, places parameter slots, and runs the plan
with `Array.Empty<JsValue>()` — avoiding struct boxing entirely.

## Profile evidence

```
Call Tree (Total Time) - root: InvokeWithContextSlow (BEFORE)
3496.36 ms 100.0% 27x InvokeWithContextSlow
├─ 2994.69 ms 85.7% 56x CastHelpers.Box      ← boxing SingleValueArgs
└─  396.00 ms 11.3% 31x RunSync
```

## Tests updated

`ActivationSemanticsProofPackTests` proof-pack boundary updated:
- Removed `makeReader` closure from `UnsafeActivationShapes_DoNotUseIrActivationFastPath`
- Added `ClosureCaptureRead_UsesIrActivationFastPath` confirming the new boundary
- Updated `SimpleReturnFunction_PropagatesThrownExpression` to expect fast path
- Updated `SimpleReturnFunction_NonNumberArgumentsDoNotUseCallerOrReturnFastPaths`
  to expect general binary parameter fast path for non-number args
