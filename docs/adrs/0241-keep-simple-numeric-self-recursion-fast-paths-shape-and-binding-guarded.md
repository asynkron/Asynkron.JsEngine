# ADR 0241: Keep simple numeric self-recursion fast paths shape- and binding-guarded

## Status

Accepted

## Amendments

ADR 0245 accepts one additional constant-term plus self-call shape under the
same shape, binding, runtime-feature, and numeric guards. This ADR remains the
base boundary for simple numeric self-recursion fast paths.

## Context

Issue `autrun-ditlr1g0oyd4-64c0980227` / PR #2439 followed the existing
Fibonacci self-recursion fast path recorded in
`docs/performance/fib-simple-numeric-self-recursion.md`. The recurring
optimizer run selected `recursion-lite` after a fresh benchmark still showed a
focused recursion owner loss:

```text
profile                 asynkron_ms  jint_ms  delta
recursion-lite                  378      191  Jint 1.98x faster
```

Repeated focused baseline rows averaged about 361.3 ms for Asynkron. The CPU
profile was dominated by recursive one-argument calls through
`SyncFunctionInvoker.InvokeWithContextSlow`, `ExecutionPlanRunner`, and
`ExecuteProgramCall`. The selected workload contained strict factorial and
sum-to linear recurrences:

```js
function factorial(n) {
    if (n <= 1) return 1;
    return n * factorial(n - 1);
}

function sumTo(n) {
    if (n <= 0) return 0;
    return n + sumTo(n - 1);
}
```

Those source shapes are narrow enough to evaluate iteratively without
re-entering function invocation for each recursive step, but only if the runtime
keeps the existing semantic guards from the Fibonacci fast path. JavaScript can
reassign the recursive name, pass non-integer values, or introduce observable
activation/class/private/super behavior that makes a source-shaped recurrence
unsafe to shortcut.

## Decision

Keep the simple numeric self-recursion fast path as a narrow
`SyncFunctionInvoker` optimization, not a generic recurrence framework.

The retained fast path may recognize only one-simple-parameter functions with a
two-statement body:

1. an `if (param <= smallInteger) return ...;` base case whose return is either
   the parameter itself or a small integer numeric literal; and
2. a binary return expression made from either two recursive
   `self(param - positiveInteger)` calls added together, or one parameter term
   combined with one recursive call by addition or multiplication.

At invocation time, keep the runtime gates decisive:

- argument zero must be a finite number;
- accepted iterative execution is limited to integer inputs at or below the
  fast-path maximum;
- the recursive binding must still resolve to the same `SyncFunctionInvoker`;
- class constructors, arrow functions, async/generator/default-derived
  constructors, home-object/private-name/super state, captured private scopes,
  and instance fields stay on the existing fallback; and
- non-integer inputs, `NaN`, infinity, oversized inputs, reassigned recursive
  names, and unsupported source shapes must keep ordinary invocation
  semantics.

Do not widen this boundary from benchmark names, function names, source text
alone, or the mere presence of a self-call. Future recursion optimizations must
start from a current selected-profile CPU owner, add focused fallback tests for
dynamic binding and numeric edge cases, and retain the code only when repeated
focused timings clear the issue threshold.

## Consequences

- `recursion-lite` can run its strict factorial and sum-to workload through an
  iterative stack-buffer path instead of recursive interpreter activation.
- The retained PR #2439 evidence was:
  - baseline `recursion-lite` Asynkron average: about 361.3 ms;
  - final focused rows: `10` / `10` / `10` ms Asynkron versus `180` / `180` /
    `179` ms Jint;
  - focused recursion tests: 9 tests passed; and
  - review-stage `rtk make quality`, Release source build, focused recursion
    tests, and focused benchmark sanity check passed.
- Future recursion work should extend the shape detector only when the
  JavaScript semantic boundary is equally explicit. Activation reuse,
  expression-program call routing, or broader recurrence solving remain
  separate decisions requiring their own profiling and proof.
- The performance note remains the measurement transcript, while this ADR owns
  the architectural boundary for the retained fast path.

## Related

- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/FoundationTests.cs`
- `docs/performance/fib-simple-numeric-self-recursion.md`
- `docs/performance/recursion-linear-self-recursion.md`
- `docs/adrs/0245-keep-constant-term-self-recursion-widening-guarded.md`
- `.claude/rules/performance-profiling-guardrails.md`
