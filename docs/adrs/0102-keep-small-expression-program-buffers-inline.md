# ADR 0102: Keep small ExpressionProgram buffers inline

## Status

Accepted

## Context

Issue `autrun-diqxxf5b04kw-a516a2bc0a` / PR #1685 selected `classdef` from the
required `rtk ./benchmark.sh` baseline because it was one of the largest current
Asynkron-vs-Jint losses. The broad table showed:

```text
classdef  asynkron_ms=1279  jint_ms=339  Jint 3.77x faster
```

A focused pre-change run still showed a clear loser:

```text
classdef  asynkron_ms=1537  jint_ms=276  Jint 5.57x faster
```

The required CPU profile,
`rtk ./tools/profile classdef --cpu --calltree-depth 40 --calltree-width 40`,
showed repeated `EvaluateExpressionProgram` buffer acquisition under
constructor, `super(...)`, `Array.prototype.map`, and method-call paths. The
profile cost came from frequent small expression programs paying
`AcquireExpressionBuffers` / `ReturnCachedExpressionBuffers` and
`SharedArrayPool<JsValue>.Rent` / `Return` overhead.

The selected script had only 14 lowered expression programs, and the storage
diagnostic showed every max stack depth fit within eight slots:

```text
max_stack_depth_histogram:
  depth=1: 6
  depth=2: 6
  depth=3: 1
  depth=7: 1
```

## Decision

Keep small `ExpressionProgram` runtime stack and flag buffers inline in
`ExecutionPlanRunner.EvaluateExpressionProgram`.

The durable policy is:

1. use stack-local inline buffers for expression programs whose max stack depth
   is within the proven small-program threshold;
2. keep the existing pooled-array path for larger expression programs;
3. preserve the packed expression flag representation for optional-chain and
   related per-slot side state; and
4. treat the inline threshold as profile-owned evidence, not a generic constant
   to raise without a current `MaxStackDepth` distribution and semantic proof.

The accepted PR used an inline stack capacity of eight `JsValue` slots and one
inline `ulong` flag word. That matched the measured `classdef` stack-depth
distribution while preserving the generic pooled fallback for deeper programs.

## Consequences

- Common short expression programs avoid ArrayPool rent/return traffic in the
  hot runner path.
- Larger or future deeper expression programs still use pooled arrays rather
  than risking excessive frame size.
- Future expression-runtime allocation work must keep CPU and memory evidence
  separate: a CPU profile can identify buffer-rent overhead, but allocation
  claims still need the matching memory or runner proof.
- Any future threshold change should include the selected benchmark, the
  `MaxStackDepth` histogram or equivalent diagnostic, focused expression
  runtime tests, the AST-eval seam scan, and `rtk ./tools/profile forloop
  --memory`.

## Related

- `docs/performance/classdef-inline-expression-buffers.md`
- `docs/adrs/0095-keep-expression-program-compaction-measurement-led.md`
- `docs/adrs/0097-keep-expression-program-operation-storage-owner-encoded.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/expression-bytecode-packing.md`
