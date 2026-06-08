# Classdef Production Slot Storage Cache

Date: 2026-06-08

## Selected Profile

The required full benchmark baseline kept `classdef` as the selected bounded
slice:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                         10648      869  Jint 12.25x faster
```

The current data therefore did not supersede the investigation handoff. The
slice stayed on class constructor and `super()` dispatch instead of widening to
broader activation or property-access losses.

## Profile Finding

The required CPU profile was captured three times:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

All three profiles kept the hot path in production unified-bytecode constructor
dispatch:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        SyncFunctionInvoker.TryInvokeProductionUnifiedBytecode
          UnifiedBytecodeVirtualMachine.ExecutePreparedSuperConstruct
            UnifiedBytecodeVirtualMachine.ConstructNoSpread
              SharedArrayPool<JsValue>.Rent / Return
```

The profiles also showed the `Array.map` callback tail, but the repeatable
constructor/super subtree was the narrower owner surface and matched the prior
classdef reports.

## Change

`SyncFunctionInvoker` now caches a small production unified-bytecode slot array
per class-constructor invoker, up to 64 slots. Ordinary functions, recursive or
concurrent re-entry, and larger slot programs keep using
`ArrayPool<JsValue>.Shared`, so the change does not alter the existing
production-bytecode admission gates or constructor semantics.

The cached buffer is cleared before release back to the invoker to avoid
retaining per-call `JsValue` object references.

## Final Signal

A rebuilt focused A/B check was used because the full benchmark row is noisy.
The first retained patch measured as:

```text
unpatched classdef: 4360 ms, 7407 ms, 1429 ms
patched classdef:   2906 ms, 3104 ms, 1257 ms
```

The unpatched average was about 4399 ms. The patched average was about 2422
ms. That is about a 45% Asynkron-side improvement for the focused `classdef`
measurement, clearing the requested 10% threshold.

After the first implementation, `rtk ./tools/profile forloop --memory` showed a
high allocation signal. Rechecking with the runtime patch reverted produced the
same current-main signal, about 968 MB total allocated, so the final
implementation kept the cache scoped to class constructors only and did not
claim a `forloop` allocation improvement.

The final class-constructor-only patch measured as:

```text
patched classdef: 2235 ms, 2251 ms, 1789 ms
```

That averages about 2092 ms, still about 52% faster than the rebuilt unpatched
focused average.

An additional post-change profile no longer showed `SharedArrayPool<JsValue>`
rent/return as the constructor hot subtree. Remaining costs were constructor
and `super()` dispatch, property stores, and `Array.map` callback invocation.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v minimal
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ClassConstructorActivationAdmissionTests|FullyQualifiedName~ClassStatementTests|FullyQualifiedName~ClassSuperSemanticsTests|FullyQualifiedName~ClassElementEvalTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk ./benchmark.sh
rtk ./benchmark.sh classdef
rtk ./benchmark.sh --no-build classdef
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

Results:

- Release library build passed.
- Focused class constructor, class statement, class element, and super
  semantics tests passed: 60 tests.
- Full benchmark selected `classdef` as a current loss.
- Rebuilt focused A/B timings cleared the requested 10% threshold.
- AST-eval seam scan returned no matches in execution-plan runner files.
- `forloop --memory` matched current-main behavior with and without the patch,
  about 968 MB total allocated.
- The canonical internal quality gate remains `rtk make quality` and is
  delegated to the orchestrator-run verification stage.
