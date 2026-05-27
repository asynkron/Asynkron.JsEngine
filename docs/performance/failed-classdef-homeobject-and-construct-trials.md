# Failed Classdef Home-Object and Construct Trials

Date: 2026-05-27

## Selected Profile

The required full benchmark baseline still selected `classdef` as a large
Asynkron-vs-Jint loss:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                          1249      298  Jint 4.19x faster
```

Repeated focused baseline runs were noisy but clustered around 707 ms:

```text
classdef                         733      273  Jint 2.68x faster
classdef                         712      276  Jint 2.58x faster
classdef                         676      320  Jint 2.11x faster
```

The required CPU profile was:

```bash
rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40
```

The sampled call tree still pointed at constructor and `super(...)` dispatch:

```text
ExecuteProgramConstructNoSpread
  ReflectHelper.Construct
    SyncFunctionInvoker.InvokeWithContext
      SyncFunctionInvoker.InvokeWithContextSlow
        ExecutionPlanRunner.RunSync
          ExecuteProgramSuperConstruct
            ExecuteProgramConstructNoSpread
              ReflectHelper.Construct
```

The `dogs.map(d => d.speak())` tail remained visible too, but it was not large
enough to justify a speculative broad callback rewrite in this run.

## Trials

### Typed no-spread construct shortcut

I tried a narrow expression-runner shortcut for typed no-spread constructors
that bypassed part of `ReflectHelper.Construct` while leaving proxies, host
constructors, spread calls, and generic construction on the existing path.

Focused timings after the trial were slower than the focused baseline:

```text
classdef                         761      283  Jint 2.69x faster
classdef                         773      270  Jint 2.86x faster
classdef                         735      267  Jint 2.75x faster
```

The change was reverted.

### Simple parameter binding shortcut

I tried avoiding the temporary parameter-name list for simple parameter lists.
This was semantically small but broader than the classdef owner surface.

Focused timings did not show a repeatable 10% win:

```text
classdef                         693      296  Jint 2.34x faster
classdef                         739      265  Jint 2.79x faster
classdef                         730      252  Jint 2.90x faster
```

The change was reverted.

### Class method home-object activation gate

ADR 0193 documents that simple class methods may use simple IR activation when
their lowered return program has no `super` dependency. The current code still
turns `_canUseSimpleIrActivationFastBase` off in `SetHomeObject`, so I tested
removing only that invalidation and relying on the existing
`CanUseSimpleIrActivationHomeObjectPath(...)` guard.

Focused timings were again slower than baseline:

```text
classdef                         842      288  Jint 2.92x faster
classdef                         747      273  Jint 2.74x faster
classdef                         791      267  Jint 2.96x faster
```

The change was reverted. The result suggests the current simple activation path
for this class-method shape is not cheaper than the full invocation path under
today's runner, even though the semantic guard remains worth preserving for
future activation work.

### Super constructor this-init lookup reorder (reverted)

I tried a bounded reorder in `ExecuteProgramSuperConstruct` that moved the
generic `ThisInitialized` environment walk ahead of the constructor-`this`
resolution path. The goal was to reduce super-constructor dispatch overhead in
derived constructor hot paths by avoiding extra environment-resolution work.

The runtime change did not survive because it regressed correctness for
direct-eval arrow constructor cases: the reordered lookup could pick a stale
`ThisInitialized` binding before the constructor-owned environment chain was
resolved. That broke `super()` initialization semantics in this edge shape.

The attempt was reverted in follow-up (`68496be5`) and recorded as failed
evidence rather than retained as a dormant optimization.

## Final Signal

No runtime change was retained. After reverting the runtime trials, the focused
profile returned to the baseline range:

```text
classdef                         739      283  Jint 2.61x faster
```

Final timestamp: 2026-05-27T03:30:00Z
Final signal: `classdef` Asynkron = 739 ms after reverting runtime trials
Signal delta: no retained speedup; runtime changes reverted because they did
not meet the required repeatable 10% improvement threshold.

## Outcome

This run is a failed-attempt evidence slice. It keeps the repository source
unchanged outside this report and records that the obvious no-spread construct,
simple parameter-list, and home-object activation variants did not survive the
noise-controlled focused benchmark check.
