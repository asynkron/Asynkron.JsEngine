# ADR 0120: Keep string append rope flattening consumer-driven

## Status

Accepted

## Context

Issue `autrun-dirph659s868-e8df189b62` / PR #1799 selected `stringops` from
the required `rtk ./benchmark.sh` baseline:

```text
stringops  asynkron_ms=1086  jint_ms=352  Jint 3.09x faster
```

The focused CPU profile,
`rtk ./tools/profile stringops --cpu --calltree-depth 40 --calltree-width 40`,
showed the owned hot path under slot compound assignment:

```text
HandleCompoundAssignmentSlotSlow
  -> ProfileApplyBinaryOperator
  -> ApplyBinaryOperator
  -> AddValue
  -> AddStringValue
  -> JsRopeString.Concat
  -> JsRopeString.GetString
  -> JsRopeString.Flatten
```

The workload repeatedly appended a primitive string and consumed the completed
string later through `toUpperCase`, `split`, and `join`. `JsRopeString` already
flattened with an explicit stack, but its depth guard forced a flatten after 32
appends. That made the append loop repeatedly rebuild the growing string before
any consumer needed flat contents.

The accepted delivery raised the forced-flatten depth for ropes and added a
primitive `string + string` fast path inside the existing profiling compound-add
path. Final selected-profile runs were 551 ms, 555 ms, and 566 ms, so the
slowest final run was 47.9% faster than the baseline.

## Decision

Keep repeated primitive string append optimization consumer-driven:

1. `JsRopeString` may defer flattening for deep append chains because flattening
   uses an explicit stack rather than recursive traversal.
2. Primitive `string + string` slot compound addition may stay on a direct rope
   concatenation path when both operands are already tagged JavaScript strings.
3. Generic addition, object/string coercion, BigInt, symbols, and mixed operand
   types must continue through the existing generic addition path.
4. String-operation performance claims must keep the baseline, CPU call tree,
   repeated final selected-profile runs, and consumer correctness test separate.

This is a narrow runtime-owner decision, not a license to flatten earlier in
the append loop or to widen string fast paths across coercive JavaScript
addition semantics.

## Consequences

- Repeated primitive string appends can accumulate as ropes until an actual
  string consumer needs flat content.
- The slot compound-assignment path avoids the generic binary-operator dispatch
  layer for the proven primitive string/string shape.
- Observable coercion, side effects, BigInt errors, symbol errors, and mixed
  numeric/string addition remain owned by the generic addition implementation.
- Future `stringops` slices should prove whether the cost is append-loop
  flattening, consumer flattening, or another string built-in before changing
  `JsRopeString`, evaluator addition, or standard-library string methods.

## Related

- `docs/performance/stringops-rope-append-fast-path.md`
- `.claude/rules/performance-profiling-guardrails.md`
