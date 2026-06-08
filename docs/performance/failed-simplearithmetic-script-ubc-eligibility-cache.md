# Failed: simplearithmetic script production-bytecode eligibility cache

Date: 2026-06-08

## Selected Profile

The required full comparison table selected `simplearithmetic` because it was
the largest current Asynkron-vs-Jint ratio loss:

```text
profile                    asynkron_ms  jint_ms  delta
simplearithmetic                   707       80  Jint 8.84x faster
promise                           2248      529  Jint 4.25x faster
classdef                          1066      285  Jint 3.74x faster
arrayops                          1387      385  Jint 3.60x faster
```

Baseline timestamp: 2026-06-08T18:57:21Z
Baseline signal: `simplearithmetic` Asynkron focused rows = 690, 615, 616 ms

The current data superseded the investigation's classdef recommendation. The
latest classdef slot-storage cache is already on `origin/main` as PR #3505, and
the active sibling issue list showed no other active Optimizer child besides
this run.

## CPU Profile Evidence

The required profile command was run three times:

```bash
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
```

All three runs used the comparable wrapped workload:

```text
ProfileRunner --wrap-iife simplearithmetic
```

The repeated filtered shape was script production-bytecode eligibility and
compile work, not the older `Math.sqrt` / `Math.pow` host-dispatch owner:

```text
TypedAstEvaluator.TryRunScriptViaProductionUnifiedBytecode
UnifiedBytecodeProductionEligibility.EvaluateCore
UnifiedBytecodeCompiler.TryCompile
UnifiedBytecodeVirtualMachine.Execute
UnifiedBytecodeVirtualMachine.ExecutePreparedCall
TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContext
```

The profile wrapper's default call-tree root, `ExecuteInstructionLoop`, did not
match because this shape now enters the production unified-bytecode VM.

## Trial

I tried caching the script production unified-bytecode eligibility result and
compiled program on `ScriptPlanCache`, then using that cached result from script
execution. The intended owner was the repeated
`UnifiedBytecodeProductionEligibility.EvaluateScript` / `UnifiedBytecodeCompiler.TryCompile`
work observed in the CPU profiles.

The code built cleanly after changing the cache-ready flag from a volatile bool
to an int-backed `Volatile.Read` / `Volatile.Write` pattern:

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v minimal
```

```text
ok dotnet build: 2 projects, 0 errors, 0 warnings
```

## Result

Focused rows with the experiment did not clear the 10% bar:

```text
simplearithmetic               2249      166  Jint 13.55x faster
simplearithmetic                975     1084  Asynkron 1.11x faster
simplearithmetic               4729     1306  Jint 3.62x faster
simplearithmetic                614       76  Jint 8.08x faster
```

The best retained-looking row only matched the low end of the baseline range,
while the repeated rows were noisy or worse. The runtime edit was reverted.

Final timestamp: 2026-06-08T19:02:57Z
Final signal: `simplearithmetic` post-revert focused rows = 1430, 2256, 1416 ms
Signal delta: no retained speedup; script eligibility cache experiment reverted
because repeated timings did not show a stable >=10% improvement over the
690/615/616 ms baseline rows.

## Outcome

No runtime change is retained. This run preserves the measured failed attempt so
future `simplearithmetic` work does not retry a script-level eligibility/program
cache without a quieter A/B setup or deeper proof that the cached
`UnifiedBytecodeProgram` itself is not changing the workload shape.

The remaining signal is noisy enough that future optimizer runs should either
use a more isolated script-production-bytecode workload or collect same-window
patched/unpatched rows before committing another broad script-cache change.

## Commands Run

```bash
rtk ./benchmark.sh
rtk ./benchmark.sh simplearithmetic
rtk ./tools/profile simplearithmetic --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v minimal
rtk git diff --check
```
