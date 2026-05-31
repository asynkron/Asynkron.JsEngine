# Failed: classdef — Remove _argumentsObjectNeeded from constructor fast-path guards

## Summary

Removing the `_argumentsObjectNeeded` guard from `CanUseSimpleBaseClassConstructorFastPath`
and `CanUseSimpleDerivedClassConstructorFastPath` eliminated struct boxing of constructor
arguments but only produced ~6-8% wall-clock improvement on the `classdef` benchmark —
below the required 10% threshold.

## Baseline signal

Baseline timestamp: 2026-05-31T03:10:00Z
Baseline signal: classdef asynkron_ms = 712 (A/B focused median of 685, 694, 712, 760, 763 across 5 runs)
Baseline Jint: ~259ms median

## Final signal

Final timestamp: 2026-05-31T03:30:00Z
Final signal: classdef asynkron_ms = 670 (median of 648, 658, 658, 678, 682 across 5 runs, 978ms outlier excluded)
Final Jint: ~259ms median
Signal delta: −42ms / −5.9% improvement (below the 10% bar)

## Root cause analysis

### Profile finding (pre-change, 3 runs)

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

Call tree (pre-change):
```
143.02 ms 100.0%  ExecuteInstructionLoop
├─ 80.90 ms 56.6%  HandleEvaluateAndDiscard
│  └─ EvaluateExpressionProgram
│     └─ ExecuteProgramConstruct (4x, 52.9%)
│        └─ ReflectHelper.Construct
│           └─ SyncFunctionInvoker.InvokeWithContext
│              └─ SyncFunctionInvoker.InvokeWithContextSlow (71.61ms)
│                 ├─ CastHelpers.Box (35.19ms, 24.6%)   ← Dog ctor boxing
│                 ├─ RunSync (29.68ms)
│                 │  └─ ExecuteProgramSuperConstruct (9x)
│                 │     └─ InvokeWithContextSlow (24.28ms)
│                 │        ├─ CastHelpers.Box (18.85ms, 13.2%)  ← Animal ctor boxing
└─ HandleClassDeclaration (16.15ms, 11.3%)
```

Total boxing: 54ms = 37.8% of profile time.

### Why boxing was happening

`CanUseSimpleBaseClassConstructorFastPath` and `CanUseSimpleDerivedClassConstructorFastPath`
both gate on `_argumentsObjectNeeded`. For any non-arrow function whose body does not
explicitly shadow `arguments` with `let arguments`, `_argumentsObjectNeeded = true` — even
when `_usesArguments = false` and `_needsArgumentsBinding = false`.

For the `classdef` benchmark constructors:
- `Animal.constructor(name) { this.name = name; }` — `_argumentsObjectNeeded = true` but
  `_usesArguments = false` and `_needsArgumentsBinding = false`
- `Dog.constructor(name, breed) { super(name); this.breed = breed; }` — same flags

Both constructors fail their respective fast-path eligibility checks on `_argumentsObjectNeeded`,
falling through to the full `InvokeWithContextSlow` slow path where `new ExecutionPlanRunner(...,
arguments, ...)` boxes the `TwoValueArgs`/`SingleValueArgs` struct to `IReadOnlyList<JsValue>`.

This is the same pattern fixed for `CanUseSimpleIrActivationFastPath` in
`closure-simple-activation-fast-path.md` — but the constructor-specific fast paths were not
updated at that time.

## Change attempted

Removed `_argumentsObjectNeeded ||` from both fast-path guards and added the same
explanatory comment used in `CanUseSimpleIrActivationFastPath`:

```csharp
// _argumentsObjectNeeded is intentionally omitted: the fast path skips creating the
// arguments object, which is safe when _usesArguments and _needsArgumentsBinding are
// both false — the IR plan has no instructions that access the arguments binding.
_usesArguments ||
_needsArgumentsBinding ||
```

## Post-change profile

After the change, the call tree confirmed boxing was eliminated:

```
123.04 ms 100.0%  ExecuteInstructionLoop
├─ TryInvokeSimpleDerivedClassConstructorFastPath (Dog, 32.5%)
│  └─ RunSync (27.0%)
│     └─ ExecuteProgramSuperConstruct (Animal via super)
│        └─ TryInvokeSimpleBaseClassConstructorFastPath (9.2%)
│           └─ RunSync (8.1%)
```

No `CastHelpers.Box` in the hot path. Profile time improved 143ms → 123ms = **14.0% faster**.

## Why it fell below the 10% bar

Despite eliminating all boxing, the `classdef` benchmark includes substantial work beyond
constructor invocation:

- `"Dog" + i` and `"Breed" + (i % 10)` string concatenation per iteration
- `dogs.push(...)` array growth and push
- `dogs.map(d => d.speak())` map + arrow callback + speak() method invocation

These operations are unaffected by the constructor optimization. The constructor boxing
was ~37.8% of the profiler's sampled tree, but the full benchmark wall-clock includes
additional overhead that dilutes the gain to ~6-8%.

## Next steps for this benchmark

The remaining hot path after removing boxing is:
- `TryInvokeSimpleDerivedClassConstructorFastPath` → `RunSync` (27% of profile)
  - Dog constructor body: `super(name)` + `this.breed = breed` property assignment
- String concatenation and array push operations (outside constructor)

Potential future wins:
1. Optimize the `super()` call dispatch within the derived constructor fast path — each
   `super(name)` still goes through `ExecuteProgramSuperConstruct` → `ReflectHelper.Construct`
   → `InvokeWithContext` → `InvokeWithContextSlow`. If a direct super-call path existed
   that skipped `ReflectHelper.Construct`, it could shave 12-15% of profile time.
2. Cache the `PropertyHandle` for `this.name` / `this.breed` assignments across calls —
   each assignment currently re-resolves the property slot via `PropertyHandle.Resolve`.
3. Attack string concatenation overhead in the benchmark loop (applies to all benchmarks).

## Outcome

Change reverted. `_argumentsObjectNeeded` guard left in place in both constructor fast-path
methods. The fix is logically correct and semantically safe (confirmed by profile), but the
wall-clock improvement does not clear the 10% threshold due to the benchmark's mixed workload.
