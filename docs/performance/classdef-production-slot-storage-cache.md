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

## Current-main Residual Reprofile

On 2026-06-09, issue #3531 reprofiled `classdef` after PR #3505 and ADR 0374
were already on `origin/main` (`0cdac63ed`). The task branch matched
`origin/main` before profiling, so the evidence is current-main evidence rather
than branch-local evidence.

The refreshed selected row was:

```text
profile                 asynkron_ms  jint_ms  delta
classdef                        858      255  Jint 3.36x faster
```

The CPU profile kept the largest residual family in constructor and `super()`
dispatch, not in the slot-cache rent/return owner that PR #3505 removed:

```text
ExecuteInstructionLoop
  HandleEvaluateAndDiscard
    EvaluateExpressionProgram
      ExecuteProgramConstruct
        ExecuteProgramConstructNoSpread
          ReflectHelper.Construct
            SyncFunctionInvoker.InvokeWithContextSlow
              TryInvokeProductionUnifiedBytecode
                TryGetProductionUnifiedBytecodeProgram
                  UnifiedBytecodeProductionEligibility.Evaluate
                    UnifiedBytecodeCompiler.TryCompile
                UnifiedBytecodeVirtualMachine.Execute
                  ExecutePreparedSuperConstruct
                    ConstructNoSpread
```

The sampled residual split under that owner was:

- `TryGetProductionUnifiedBytecodeProgram` / eligibility / compile:
  `30.80 ms`, `17.5%` of the `ExecuteInstructionLoop` root.
- `ExecutePreparedSuperConstruct` / `ConstructNoSpread`: `21.79 ms`, `12.4%`.
- Simple derived-constructor environment creation: `10.50 ms`, `6.0%`.

The same profile also showed the `Array.prototype.map` callback tail
(`ArrayPrototype.Map` / `InvokeArrayIterationCallback`, `30.25 ms`, `17.2%`)
as a separate residual family. Keep that separate from constructor/super
dispatch unless a future profile chooses callback invocation as the single
bounded owner.

A descriptor-keyed accepted-program cache on `ExecutionPlan` was trialed
locally because the first profile sampled eligibility/compile under
constructor dispatch. It was not retained: rebuilt patched timing rows were
`887 ms`, `902 ms`, and `834 ms` against the current-main `858 ms` row, and the
follow-up profile still showed the expensive compile sample as a first-hit
per-plan effect rather than a repeatable loop owner.

Future work should therefore keep ADR 0374 intact and treat the next measured
constructor/super slice as invocation/environment/super-dispatch work, not as
a broader slot-storage cache or a plan-level accepted-program cache.

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
