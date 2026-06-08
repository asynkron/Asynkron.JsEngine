# ADR 0374: Keep classdef production slot storage cache invoker-owned

## Status

Accepted

## Context

Issue `autrun-dj0y1gbpfxlc-79c4e12a6e` / PR #3505 continued the recurring
optimizer work on the `classdef` benchmark. The selected baseline row was:

```text
profile                    asynkron_ms  jint_ms  delta
classdef                         10648      869  Jint 12.25x faster
```

Repeated CPU profiles kept the owner in production unified-bytecode class
constructor dispatch, specifically through `SyncFunctionInvoker` and
`ExecutePreparedSuperConstruct`, with `SharedArrayPool<JsValue>.Rent` /
`Return` in the constructor/super subtree.

Earlier `classdef` attempts had already shown that broader construction,
parameter-binding, home-object, and ordinary this-initialization shortcuts can
look adjacent to this path but fail the repeated selected-profile timing gate.
This delivery therefore stayed at the invoker-owned slot-storage boundary for
class constructors only.

## Decision

Cache a small reusable production unified-bytecode slot array on each
`SyncFunctionInvoker` for class constructors only.

The cache is deliberately bounded and owner-local:

- only `IsClassConstructor` invokers may use it;
- only programs with at most 64 slots use the cached buffer;
- recursive or concurrent re-entry falls back to `ArrayPool<JsValue>.Shared`;
- ordinary functions continue to use the existing pooled path; and
- the used span is cleared before the buffer is released back to the invoker so
  per-call `JsValue` references are not retained.

Do not generalize this as an activation-wide slot-storage cache or an
`ArrayPool` policy change without fresh owner evidence. The retained win came
from a repeatedly sampled class-constructor production-bytecode subtree, not
from changing function invocation storage globally.

## Consequences

- The `classdef` constructor path avoids repeated small array-pool rent/return
  overhead when the same class-constructor invoker is reused.
- Re-entrant and larger-slot programs preserve the existing pooled fallback, so
  the optimization does not add shared mutable slot storage across concurrent
  executions.
- Future `classdef` work should first distinguish constructor-owned slot
  storage, `super(...)` dispatch, property stores, string construction, and
  `Array.map` callback costs before widening adjacent runtime surfaces.
- Memory claims stay separate from CPU claims. The delivery rechecked
  `forloop --memory` after a high allocation signal and found current-main
  behavior matched the patch at about 968 MB, so the retained artifact claims a
  `classdef` CPU/timing win only.
- The retained evidence for PR #3505 was:
  - full selected baseline: `classdef` Asynkron `10648 ms`, Jint `869 ms`;
  - final focused row after rebase: Asynkron `1865 ms`, Jint `676 ms`;
  - rebuilt focused A/B average: unpatched about `4399 ms`, patched about
    `2092 ms`;
  - focused class constructor/statement/super/element proof pack: 60 tests
    passed;
  - runner AST-seam scan: no `EvaluateExpression(` or
    `ProfileEvaluateExpression(` hits; and
  - `forloop --memory`: about `968 MB` with and without this patch.

## Related

- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `docs/performance/classdef-production-slot-storage-cache.md`
- `docs/rules/performance-profiling-guardrails.md`
- `docs/adrs/0214-keep-classdef-homeobject-and-construct-retries-profile-proven.md`
- `docs/adrs/0217-keep-derived-constructor-this-init-lookup-local-first.md`

## Allocation Note

The local `rtk faktorial-api adr-next` helper was unavailable in this worktree
(`No such file or directory`), so this learn pass used the host runtime
allocator endpoint `POST /api/adrs/next`, which returned `{"adr_id":374}`.
The prefix `0374` was checked free before writing.
