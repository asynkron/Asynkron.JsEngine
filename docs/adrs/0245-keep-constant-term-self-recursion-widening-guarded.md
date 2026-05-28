# ADR 0245: Keep constant-term self-recursion widening guarded

## Status

Accepted

## Context

Issue #2450 / PR #2465 was a roadmap follow-up to ADR 0241. ADR 0241
accepted a guarded `SyncFunctionInvoker` fast path for strict simple numeric
self-recursion, covering Fibonacci-style self-call addition and parameter-term
linear recurrences such as factorial and sum-to.

The follow-up widened that boundary by exactly one adjacent source shape:

```js
function countUp(n) {
    if (n <= 0) return 0;
    return 1 + countUp(n - 1);
}
```

The same semantic risks still apply. JavaScript can reassign the recursive
name, pass non-integer values, use unsupported function/runtime features, or
make evaluation observable in ways that a source-shaped recurrence shortcut
must not hide. The delivery therefore added a dedicated
`AddConstantAndSelfCall` operation and stored the small integer constant in the
existing fast-path descriptor instead of introducing a generic recurrence
solver.

Focused proof covered the existing ADR 0241 recursion tests plus the new
constant-left, constant-right, non-integer fallback, and reassigned-name
fallback cases. The build-stage proof command reported 9 passing tests. No
new benchmark win was claimed because the delivery did not add a selected
profile workload that separately exercises this constant-term branch.

## Decision

Extend ADR 0241 with one additional accepted shape: a small integer constant
added to exactly one self-call of `self(param - positiveInteger)`, with either
operand order.

The widened fast path remains owned by `SyncFunctionInvoker` and may apply
only when all ADR 0241 guard families still hold:

1. the function is a strict sync function with one simple parameter;
2. the body has the existing two-statement guarded-base-case shape;
3. the recursive return expression is `constant + self(param - delta)` or
   `self(param - delta) + constant`;
4. the constant and delta are small integers, and delta is positive;
5. invocation receives a finite integer input at or below the fast-path
   maximum; and
6. the recursive name still resolves to the same `SyncFunctionInvoker` at
   runtime.

Unsupported functions, unsupported arithmetic shapes, constant expressions
that are not numeric literals, non-integer inputs, `NaN`, infinity, oversized
inputs, and reassigned recursive names stay on ordinary invocation semantics.

Do not treat this as permission for general recurrence solving, source-text
heuristics, benchmark-name heuristics, or broader arithmetic normalization.
Future widening must add one explicit source shape at a time, preserve the
binding and numeric fallback proofs, and attach comparable profile evidence
before making any performance-improvement claim.

## Consequences

- Simple count-up style recurrences can use the existing iterative stack-buffer
  execution path for bounded integer inputs.
- ADR 0241 remains the base architectural boundary; this ADR is a narrow
  amendment for constant-term plus self-call addition only.
- Future recursion follow-ups should pair positive accepted-shape coverage
  with negative non-integer and reassigned-recursive-name coverage.
- Performance notes should be updated only when the selected workload actually
  exercises the newly admitted shape and includes comparable before/after
  timing or profile evidence.

## Related

- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs`
- `tests/Asynkron.JsEngine.Tests/FoundationTests.cs`
- `docs/adrs/0241-keep-simple-numeric-self-recursion-fast-paths-shape-and-binding-guarded.md`
- `docs/performance/recursion-linear-self-recursion.md`
- `.claude/rules/performance-profiling-guardrails.md`
