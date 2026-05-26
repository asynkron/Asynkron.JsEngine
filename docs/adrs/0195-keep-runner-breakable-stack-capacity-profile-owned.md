# ADR 0195: Keep runner breakable stack capacity profile-owned

## Status

Accepted

## Context

Issue `autrun-dissu2eufu0w-31ee6d51c0` / PR #2189 selected
`activation-arguments-lite` from the optimizer automation baseline because it
was the largest current Asynkron-vs-Jint loss:

```text
activation-arguments-lite  asynkron_ms=2801  jint_ms=529  Jint 5.29x faster
```

The focused CPU profile rooted at `InvokeWithContextSlow` showed that the
strict observable-arguments workload was no longer dominated only by arguments
object or lexical setup. Every invocation entered a loop and immediately paid
first-push growth for the runner's breakable-frame stack:

```text
ExecuteInstructionLoop
  HandleBreakableEnter
    Stack<BreakableFrame>.PushWithResize
      Stack<BreakableFrame>.Grow
```

`BreakableState` is runtime side state for loops and switches. It tracks active
break/continue frames; it is not the semantic owner of label resolution,
cleanup emission, iterator close, or environment unwinding. The profiler
identified a storage-capacity cost: the default zero-capacity `Stack<T>` grows
on the first push even though common activation paths have shallow loop/switch
nesting.

## Decision

Keep runner breakable-stack capacity work profile-owned and capacity-only.

`ExecutionPlanRunner.BreakableState` may initialize `BreakableStack` with a
small capacity of four frames so the common loop/switch activation path avoids
the per-invocation first-grow allocation and copy.

Future changes in this area must preserve these boundaries:

1. prove the selected workload is actually paying `BreakableStack` growth or
   another runner side-state capacity cost before changing initial capacity;
2. keep active frame count, frame order, label matching, and break/continue
   completion behavior unchanged;
3. keep emitted cleanup, `try`/`finally`, `IteratorClose`, `with`, generator,
   async, and async-generator suspension behavior on their existing owners;
4. do not replace `BreakableEnter` / `BreakableExit` lowering or runtime
   control-flow semantics just because a profile shows storage growth; and
5. prove retained performance with repeated selected-profile timings and a
   follow-up CPU profile that shows the targeted growth subtree disappeared.

The capacity is intentionally small. Deeper nesting still uses the existing
`Stack<T>` growth path, so this decision optimizes the shallow startup case
without making deep control-flow frames eager.

## Consequences

- Loop-heavy activation profiles can avoid the first breakable-stack resize
  without changing observable JavaScript behavior.
- Breakable-frame capacity tuning stays separate from activation arguments
  materialization, lexical-name templates, and activation environment slot
  capacity decisions from earlier ADRs.
- Control-flow correctness remains owned by the IR cleanup and labeled
  statement rules; this ADR only owns the profiled runner side-state capacity
  choice.
- If a future profile shows a different side-state stack or list growing on a
  hot path, apply the same proof pattern before pre-sizing it and do not infer a
  broader control-flow rewrite from storage evidence alone.

## Related

- `docs/performance/activation-arguments-breakable-stack-presizing.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `.claude/rules/ir-control-flow-cleanup.md`
- `docs/adrs/0173-keep-observable-arguments-setup-profile-owned-and-plan-proven.md`
- `docs/adrs/0183-keep-activation-lexical-name-templates-hoist-owned.md`
- `docs/adrs/0167-keep-sync-ir-trampoline-frame-capacity-shallow-first.md`
