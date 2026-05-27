# ADR 0214: Keep classdef home-object and construct retries profile-proven

## Status

Accepted

## Context

Issue `autrun-dit4lwfdshqo-b26070bcec` / PR #2266 selected `classdef` from the
recurring optimizer guidance because the full benchmark still showed a large
Asynkron-vs-Jint gap:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                          1249      298  Jint 4.19x faster
```

Repeated focused baseline rows were noisy but centered around 707 ms
(`733`, `712`, and `676` ms). The CPU call tree still pointed at constructor and
`super(...)` dispatch through `ExecuteProgramConstructNoSpread`,
`ReflectHelper.Construct`, `SyncFunctionInvoker.InvokeWithContextSlow`, and
`ExecutionPlanRunner.RunSync`.

The run tested three plausible follow-ups to prior `classdef` work:

1. a typed no-spread constructor shortcut that bypassed part of
   `ReflectHelper.Construct` while keeping proxies, host constructors, spread
   calls, and generic construction on existing fallbacks;
2. a simple parameter-binding shortcut that avoided a temporary parameter-name
   list for simple parameter lists; and
3. removing the class-method home-object activation invalidation in
   `SetHomeObject`, relying on the existing super-free simple activation guard
   from ADR 0193.

All three were reverted. The no-spread construct trial measured `761`, `773`,
and `735` ms. The simple parameter-binding trial measured `693`, `739`, and
`730` ms. The home-object activation gate trial measured `842`, `747`, and
`791` ms. After reverting all runtime edits, `classdef` measured `739` ms,
which was slower than the 707 ms focused baseline average and did not satisfy
the required repeatable 10% improvement threshold.

Existing positive decisions still stand. ADR 0171 keeps typed no-spread
construct argument carriers through the construction boundary. ADR 0193 keeps
plain class methods eligible for simple IR activation only when the lowered
simple return program proves no `super` dependency. ADR 0177 already records
that cleaner post-boundary runner argument storage is not enough when repeated
timings regress. This issue adds a narrower negative result for the adjacent
construct, simple-parameter, and home-object invalidation retry shapes.

## Decision

Do not retain or reapply the three `classdef` retry shapes from issue
`autrun-dit4lwfdshqo-b26070bcec` unless a future run first proves a fresh owner
and then clears the selected-profile threshold with repeated timings:

1. do not bypass additional `ReflectHelper.Construct` work for typed no-spread
   construct calls solely because the call tree still includes constructor and
   `super(...)` dispatch;
2. do not add a simple parameter-binding shortcut unless current activation or
   constructor-profile evidence names parameter binding as the hot owner and
   the shortcut wins after A/B timing; and
3. do not treat ADR 0193 as permission to remove home-object fast-base
   invalidation without measurement. ADR 0193 defines the semantic eligibility
   boundary; it does not prove that the current simple activation path is
   cheaper for every class-method shape.

Future work may revisit any of these areas, but it must start from current
profile evidence, name the exact owner surface, pin the relevant class/super or
activation semantics, and keep the runtime edit reverted if repeated focused
benchmark rows miss the issue threshold.

## Consequences

- Future optimizer agents can use
  `docs/performance/failed-classdef-homeobject-and-construct-trials.md` as
  negative evidence before retrying adjacent `classdef` micro-slices.
- Constructor and `super(...)` optimization remains allowed at the
  `ReflectHelper.Construct` and expression-program construction boundaries, but
  only with current proof beyond the already-reverted shortcut.
- ADR 0193 remains the semantic policy for class-method simple activation, but
  performance work on the `SetHomeObject` invalidation or simple activation
  fast base needs its own timing proof.
- Failed attempts should continue to be documented as failed performance notes
  rather than kept as dormant abstractions or described as successful
  optimizations.

## Related

- `docs/performance/failed-classdef-homeobject-and-construct-trials.md`
- `docs/adrs/0171-keep-no-spread-construct-argument-carriers-and-super-spread-order.md`
- `docs/adrs/0177-keep-runner-argument-storage-benchmark-proven.md`
- `docs/adrs/0193-keep-class-method-simple-ir-activation-super-guarded.md`
- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/performance-profiling-guardrails.md`
