# Failed Classdef Derived Super Internal Binding Locality

Date: 2026-06-09

## Selected Profile

The required full benchmark baseline still showed `classdef` as a current
Asynkron-vs-Jint loss after the production slot-storage cache from PR #3505
was already on `origin/main`:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                          1250      412  Jint 3.03x faster
```

Other rows had larger ratios in this run, but the investigation handoff and
recent retained classdef notes scoped this child to the remaining class
constructor and `super()` dispatch owner. The branch matched `origin/main`
before profiling, so this is current-main residual evidence.

## Profile Finding

The required CPU profile was captured three times:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

All three profiles kept the selected owner under constructor and `super()`
production unified-bytecode dispatch:

```text
ExecuteInstructionLoop
  HandleEvaluateAndDiscard
    EvaluateExpressionProgram
      ExecuteProgramConstruct
        ExecuteProgramConstructNoSpread
          ReflectHelper.Construct
            SyncFunctionInvoker.InvokeWithContextSlow
              TryInvokeProductionUnifiedBytecode
```

The repeatable residual split was:

- `TryGetProductionUnifiedBytecodeProgram` / eligibility / compile as a
  first-hit sample under the constructor path.
- `UnifiedBytecodeVirtualMachine.ExecutePreparedSuperConstruct` /
  `ConstructNoSpread` as the repeated `super(...)` dispatch subtree.
- `CreateSimpleDerivedClassConstructorEnvironment` and
  `CreateSimpleBaseClassConstructorEnvironment` as smaller activation setup
  costs.
- `ArrayPrototype.Map` / callback invocation as a separate tail that should not
  be mixed with constructor/super dispatch unless a future run chooses it as
  the single bounded owner.

This matches the residual warning in
`classdef-production-slot-storage-cache.md`: the slot-storage owner was already
removed, while constructor/super dispatch and callback invocation remain.

## Trial

A small runtime edit mirrored immutable internal constructor metadata into the
derived-constructor body environment:

- `Symbol.LexicalThisEnvironment`
- `Symbol.NewTarget`
- `Symbol.ActiveFunction`

The goal was to let `ExecutePreparedSuperConstruct` resolve internal metadata
locally during `super(...)` without walking the enclosing function environment.
The edit deliberately did not duplicate `Symbol.Super` or mutable `this`
bindings, because those are updated during super construction and must remain
on the existing semantic path.

## Result

The trial did not clear the required 10% threshold and was reverted. Focused
rows after the patch were:

```text
classdef  asynkron_ms=1250  jint_ms=415  Jint 3.01x faster
classdef  asynkron_ms=1283  jint_ms=428  Jint 3.00x faster
```

The follow-up profile still showed the same constructor/super dispatch
families, with the extra local bindings appearing as added activation setup
work rather than a measurable win.

After reverting the runtime edit, a rebuilt focused row was:

```text
classdef  asynkron_ms=1740  jint_ms=523  Jint 3.33x faster
```

That final row is noisy, but it confirms no runtime change was retained. The
useful finding is negative: do not retry derived-constructor local copies of
`LexicalThisEnvironment`, `new.target`, or `ActiveFunction` as a classdef
shortcut unless a future profile isolates the enclosing-chain lookup itself as
the dominant owner.

## Commands Run

```bash
rtk git fetch origin
rtk ./benchmark.sh
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh classdef
rtk ./benchmark.sh --no-build classdef
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
rtk ./benchmark.sh classdef
```

No runtime code change is retained by this note.
