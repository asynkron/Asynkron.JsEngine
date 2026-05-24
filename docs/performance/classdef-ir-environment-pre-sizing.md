# Class Definition IR Environment Pre-Sizing

Date: 2026-05-24

## Selected Profile

`classdef` was selected from the required full benchmark baseline because it was
the largest current Asynkron-vs-Jint loss:

```text
classdef  asynkron_ms=1137  jint_ms=311  Jint 3.66x faster
```

Two focused pre-change runs showed substantial noise but confirmed the profile
remained a clear loss:

```text
classdef  asynkron_ms=1680  jint_ms=332  Jint 5.06x faster
classdef  asynkron_ms=1145  jint_ms=285  Jint 4.02x faster
```

The three pre-change Asynkron timings averaged about 1321 ms.

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The selected hot subtree was constructor invocation environment setup under
`ExecuteProgramConstruct`. The pre-change call tree showed repeated
`CreateExecutionEnvironment -> BindFunctionParameters -> DefineJsValue ->
DefineSlot -> GrowSlots -> Array.Copy` under class constructor calls.

That growth came from the IR runner appending known function-environment
bindings one by one: `this`, the this-initialized marker, `new.target`, the
active function binding, generator runner bookkeeping bindings, `super`,
optional `arguments`/function-name bindings, and simple parameters. The
environment started with no reserved capacity, so small constructors could grow
and copy the slot array during invocation.

## Change

`ExecutionPlanRunner.CreateExecutionEnvironment` now pre-sizes the function and
parameter environments for those known activation bindings before appending
them.

The change keeps the same logical slot count and binding order. It only reserves
enough backing capacity to avoid `GrowSlots` during the predictable activation
append path.

## Final Signal

After the change, three focused `classdef` comparison runs were:

```text
classdef  asynkron_ms=1094  jint_ms=383  Jint 2.86x faster
classdef  asynkron_ms=970   jint_ms=290  Jint 3.34x faster
classdef  asynkron_ms=977   jint_ms=276  Jint 3.54x faster
```

The post-change Asynkron timings averaged about 1014 ms. Compared with the
1321 ms pre-change average, that is roughly a 23% Asynkron-side improvement.
Compared with the best pre-change signal of 1137 ms, the post-change average is
still roughly 11% faster.

The follow-up CPU profile no longer showed `BindFunctionParameters -> GrowSlots
-> Array.Copy` under the constructor subtree. Remaining sampled cost shifted to
single-arg call boxing, array `push`/`map`, function invocation, and property
lookup.

## Verification

```bash
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj -c Release -v q --nologo
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~FoundationTests&FullyQualifiedName~Class" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~JsEnvironmentSlotTests|FullyQualifiedName~SlotGuardrailTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
rtk rg "EvaluateExpression\\(|ProfileEvaluateExpression\\(" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner*
rtk ./tools/profile forloop --memory
```

Results:

- Release library build passed.
- Class-focused internal tests passed: 98 tests.
- Slot/environment focused internal tests passed: 12 tests.
- AST-eval seam scan returned no matches in the execution-plan runner files.
- `forloop --memory` completed with 6.04 MB total allocated.
