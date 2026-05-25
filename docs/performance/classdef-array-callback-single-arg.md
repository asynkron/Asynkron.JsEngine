# Class Definition Array Callback Single Argument Fast Path

Date: 2026-05-26

## Selected Profile

The required `rtk ./benchmark.sh` baseline showed `fib` as the largest raw
loss, but `fib` already had a recent trampoline slice in
`docs/performance/fib-trampoline-eligibility.md`. The bounded slice selected
from the same baseline was `classdef`, which remained a clear top loss and
matched the investigation handoff around `Array.prototype.map` callback
invocation:

```text
profile                 asynkron_ms  jint_ms  delta
classdef                        981      314  Jint 3.12x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The pre-change profile showed the class construction path and the final
`dogs.map(d => d.speak())` callback path as the two relevant sampled subtrees.
The map subtree repeatedly entered:

```text
ArrayPrototype.Map
  ArrayPrototype.InvokeArrayIterationCallback
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        CastHelpers.Box
```

`InvokeArrayIterationCallback` always materialized the full array-callback
argument shape: value, index, and array. That is required when callbacks can
observe extra arguments, but simple arrow callbacks with zero or one plain
parameter cannot observe the index or array arguments.

The same profile also showed first-push growth in the per-context pending
class-field-initializer stack for derived constructor calls.

## Change

`SyncFunctionInvoker` now exposes a conservative
`CanUseArrayIterationSingleArgumentFastPath` predicate. It is true only for
non-async arrow functions with zero or one simple identifier parameter and no
parameter expressions. Array iteration methods use that predicate to invoke
those callbacks with `SingleValueArgs` instead of constructing the three-value
callback argument wrapper and index `JsValue`.

The fallback path still sends all three arguments for callbacks that can observe
them, including callbacks with index or array parameters, rest parameters, and
ordinary functions that can inspect `arguments`.

`EvaluationContext` also pre-sizes the pending class-field-initializer stack to
avoid first-push growth in derived constructor-heavy profiles.

## Final Signal

After the complete slice, repeated focused `classdef` comparison runs were:

```text
classdef                        863      265  Jint 3.26x faster
classdef                        926      281  Jint 3.30x faster
classdef                        866      281  Jint 3.08x faster
classdef                        860      274  Jint 3.14x faster
classdef                        869      287  Jint 3.03x faster
```

The post-change Asynkron timings averaged about 877 ms. Compared with the
981 ms baseline, that is roughly an 11% Asynkron-side improvement.

The follow-up CPU profile no longer showed pending class-field-initializer
stack growth in the derived constructor subtree. The array callback subtree
remained visible, with remaining cost in typed callback invocation and argument
list boxing.

## Verification

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ArrayIterationCallbacks|FullyQualifiedName~ClassSuperSemanticsTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh classdef
rtk ./benchmark.sh --no-build classdef
```

Results:

- Focused internal tests passed: 11 tests.
- Repeated selected-profile timings passed the requested 10% threshold on
  average.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
