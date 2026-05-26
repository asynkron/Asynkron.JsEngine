# ADR 0152: Keep simple numeric self-recursion fast path shape and binding guarded

## Status

Accepted

## Context

Issue `autrun-dis9soiwafzk-cc111e3a78` / PR #1994 selected `fib` from the
optimizer benchmark table because it was the largest current Asynkron-vs-Jint
loss in that run:

```text
fib  asynkron_ms=3822  jint_ms=708  Jint 5.40x faster
```

Repeated focused pre-change timings stayed in the same range, averaging about
3761 ms for Asynkron. The focused CPU profile showed the recursive
single-argument function call path dominating:

```text
TypedAstEvaluator.SyncFunctionInvoker.InvokeWithContextSlow
TypedAstEvaluator.ExecutionPlanRunner.EvaluateExpressionProgram
TypedAstEvaluator.ExecutionPlanRunner.ExecuteProgramCall
```

Earlier `fib` performance slices had already removed failed trampoline setup
for non-tail recursive operand calls and routed typed JavaScript single-argument
calls more directly. The remaining cost was the full recursive invocation tree
for a very narrow numeric recurrence:

```javascript
function fib(n) {
    if (n <= 1) return n;
    return fib(n - 1) + fib(n - 2);
}
```

The tempting broad optimization would be to treat any self-recursive function
or any function named `fib` as a closed numeric recurrence. That is unsafe in
JavaScript. Recursive names can be rebound, non-integer numbers can reach
different base cases through ordinary recursion, dynamic scope and callable
metadata can be observable, and class/async/generator/private/super/home-object
shapes carry invocation semantics that a local numeric evaluator does not
model.

## Decision

Keep the simple numeric self-recursion optimization as a runtime fast path on
`SyncFunctionInvoker`, guarded by both source shape and current invocation
state.

The accepted fast path is eligible only when:

1. the function is strict, simple, synchronous, non-generator, non-class, and
   has exactly one simple identifier parameter;
2. the body shape is exactly a small base-case return of the parameter followed
   by a binary sum of two self-calls with positive integer decrements;
3. no lexical-this environment, inner function expression, home object, private
   name scope, super constructor/prototype, captured private-name scope, or
   instance fields are present;
4. the current recursive name binding still resolves to the same
   `SyncFunctionInvoker` instance; and
5. the argument is a finite JavaScript number. Non-integers and integers above
   the bounded fast-input limit fall back to ordinary invocation.

For integer inputs within the bound, evaluate the recurrence locally with an
explicit stack buffer instead of recursively re-entering the expression
program and activation machinery. For base-case inputs, return the original
argument value so ordinary JavaScript numeric identity is preserved for values
that do not need recursion.

Do not widen this into a general recursive-function optimizer until the new
shape has its own semantic proof. A recurrence shortcut must be tied to the
specific executable body shape and to the live recursive binding, not to the
function's name or benchmark source text.

## Consequences

- `fib`-style strict integer recurrence inputs avoid the full recursive
  invocation tree and complete in the local numeric evaluator.
- Reassigned recursive names keep ordinary JavaScript semantics because the
  binding guard rejects calls through an old function object after rebinding.
- Non-integer, `NaN`, infinity, large integer, class, async, generator, private,
  `super`, home-object, and instance-field shapes remain on the full invocation
  path.
- Future recursive-call performance work should first classify the selected
  profile shape as tail call, non-tail operand call, or proven local numeric
  recurrence before changing `SyncFunctionInvoker`.
- Proof for this path should include repeated selected-profile timings, a
  follow-up CPU profile, positive strict Fibonacci coverage, non-integer
  fallback coverage, and reassigned-name binding coverage.

## Related

- `docs/performance/fib-simple-numeric-self-recursion.md`
- `docs/performance/fib-trampoline-eligibility.md`
- `docs/performance/fib-single-argument-typed-call-dispatch.md`
- `docs/adrs/0140-keep-sync-ir-trampoline-eligibility-executor-exact.md`
- `.claude/rules/performance-profiling-guardrails.md`
