# Activation Arguments Descriptor and Hoist Fast Path

Date: 2026-05-26

## Selected Profile

`activation-arguments-lite` was selected from the required `rtk ./benchmark.sh`
baseline because it was one of the largest current Asynkron-vs-Jint losses and
has a narrow function activation owner surface:

```text
activation-arguments-lite  asynkron_ms=5762  jint_ms=652  Jint 8.84x faster
```

## Profile Finding

The required CPU profile was:

```bash
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
```

The hot subtree was activation environment setup under
`ExecutionPlanRunner.CreateExecutionEnvironment`:

```text
CreateExecutionEnvironment
  HashSet<Symbol>.ConstructFrom
  CreateArgumentsObject
    JsArgumentsObject.ctor
      JsObject.DefinePropertyInternalDirect
        Dictionary.Resize
  HoistVarDeclarations
```

The benchmark is strict-mode code that actively reads `arguments`, so the
arguments object itself is observable and cannot be skipped. The profile instead
showed avoidable setup cost around descriptor dictionary growth and strict-mode
Annex B/hoist work that is not needed for this shape.

## Change

The bounded runtime slice does two things:

- `JsArgumentsObject` now pre-sizes its tracked descriptor dictionary and the
  backing `JsObject` descriptor dictionary and insertion-order list from the
  known argument count, avoiding resize churn while defining numeric argument
  properties plus the standard arguments metadata properties.
- `ExecutionPlanRunner.CreateExecutionEnvironment` now avoids building Annex B
  blocked-name sets in strict mode, and skips the function-body var/function
  hoist scan when `HoistableDeclarationsPlan` proves the body contains no
  hoistable declarations.

The change does not add recurrence infrastructure or broaden runtime semantics;
it keeps the slice limited to activation setup costs identified by the profile.

## Final Signal

After the change, repeated focused comparison runs were:

```text
activation-arguments-lite  asynkron_ms=4842  jint_ms=789   Jint 6.14x faster
activation-arguments-lite  asynkron_ms=3346  jint_ms=2860  Jint 1.17x faster
activation-arguments-lite  asynkron_ms=3438  jint_ms=750   Jint 4.58x faster
activation-arguments-lite  asynkron_ms=2859  jint_ms=553   Jint 5.17x faster
activation-arguments-lite  asynkron_ms=2233  jint_ms=576   Jint 3.88x faster
```

The first focused post-change run improved Asynkron time by about 16% versus the
5762 ms full-table baseline. The later focused runs were materially faster than
both the baseline and first post-change sample, so the improvement clears the
requested 10% threshold despite local benchmark noise.

A follow-up CPU profile completed after the final lexical-template setup change.
AC-3 required explicit before/after evidence for the residual lexical-name owner:

```text
Before (issue baseline call tree):
CreateExecutionEnvironment
  HashSet<Symbol>.ConstructFrom
  CreateArgumentsObject
    JsArgumentsObject.ctor
      JsObject.DefinePropertyInternalDirect
        Dictionary.Resize
  HoistVarDeclarations
```

```text
After (2026-05-26, this branch, same profile command):
Call Tree (Total Time) - root: InvokeWithContextSlow
... TypedAstEvaluator.ExecutionPlanRunner.CreateExecutionEnvironment
    JsEnvironment.MarkSlotsLexicalUninitialized
    TypedAstEvaluator.CreateArgumentsObject
```

The after call tree no longer shows `HashSet<Symbol>.ConstructFrom` in the hot
activation subtree, so the residual lexical-name construction owner is reduced
out of the focused CPU hotspot for `activation-arguments-lite`.

## Verification

Completed locally:

```bash
rtk dotnet build -c Release src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./benchmark.sh --no-build activation-arguments-lite
rtk ./tools/profile activation-arguments-lite --cpu --calltree-depth 40 --calltree-width 40
rtk dotnet test tests/Asynkron.JsEngine.Tests -c Release --filter "FullyQualifiedName~ActivationSemanticsProofPackTests|FullyQualifiedName~EvalFunctionTests|FullyQualifiedName~HoistingTests|FullyQualifiedName~FunctionConstructorTests" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

The canonical internal quality gate remains `rtk make quality` and is delegated
to the orchestrator-run verification stage.
