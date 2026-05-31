# ADR 0311: Admit optional named-computed read continuations in unified bytecode

## Status

Accepted

## Context

Delivery PR #2898 widened the production unified-bytecode optional read boundary
around named-then-computed chains. Earlier optional-chain work admitted bounded
forms such as `a?.b[k]`, but the review-blocking gap was the continuation shape:

```js
return a?.items[left + right].value;
```

The first optional hop must short-circuit before evaluating the computed key
when the base is nullish, but when the base is present the computed key and any
ordinary trailing named reads remain part of the same JavaScript property-read
chain. Treating the computed hop as the end of the accepted span either leaves
the trailing `.value` outside the production boundary or risks targeting the
first nullish jump before the full chain has completed.

## Decision

Admit bounded optional named-then-computed property-read chains with ordinary
trailing named read continuations when every hop remains VM-owned.

- Eligibility may accept `a?.b[key]` and `a?.b[key].c...` only when the base is
  activation-resolved, the optional start is the first named hop, the computed
  key payload is production-owned, and every trailing continuation is a
  non-optional, non-private named read.
- The compiler must emit the optional-start nullish jump so it targets past the
  entire accepted read chain, not merely past the first computed hop.
- The compiler must append ordinary `GetNamedProperty` instructions for trailing
  read continuations after `GetComputedProperty`; it must not call back into
  `ExpressionProgram`, `ExecutionPlanRunner`, or AST evaluation for the suffix.
- Unsupported adjacent forms remain pre-VM declines: optional trailing computed
  hops, optional writes/updates/deletes, calls, `super`, private names, dynamic
  lookup, and unowned computed-key payloads.

## Consequences

- `a?.items[left + right].value` can route through the production unified
  bytecode fast path when the function otherwise satisfies the production
  boundary.
- Nullish bases still short-circuit before computed-key evaluation or key
  coercion, while present bases continue through the computed read and trailing
  named reads in one VM-owned program.
- Future optional-chain widening must reason about the complete accepted span
  before patching nullish jump targets. A shape predicate that recognizes only
  the first optional/computed prefix is insufficient when ordinary continuation
  reads follow.

## Evidence

- Delivery PR #2898 merged as commit `b9d0b221`:
  `Agent: task planitem-planmanual1780240661926543000-burn-down-unified-bytecode-production-decl-55cc574fe4`.
- The carried delivery summary reports the review-blocking fix in
  `TryIsFirstBoundaryOptionalNamedThenComputedReadChainCandidate` and
  `TryAppendFirstBoundaryOptionalNamedThenComputed`, plus focused eligibility
  and invocation coverage for `a?.items[left + right].value`.
- Focused verification from the delivery stage passed:
  `rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~UnifiedBytecodeProductionEligibilityTests.Evaluate_OptionalNamedThenComputed|FullyQualifiedName~UnifiedBytecodeProductionInvocationTests.OptionalNamedThenComputed"`.
  Result: 6 tests passed.
- Adjacent optional property-read focused verification passed with 14 tests.
- The delivery-stage AST seam scan for `EvaluateExpression(` /
  `ProfileEvaluateExpression(` in runner files found no matches.

## Related

- `docs/adrs/0221-keep-unified-bytecode-property-reads-vm-owned-and-observable.md`
- `docs/adrs/0296-admit-optional-member-access-in-unified-bytecode-with-null-check-opcodes.md`
- `docs/adrs/0298-admit-multi-hop-optional-named-chains-in-unified-bytecode-jump-based-lowering.md`
- `docs/adrs/0305-admit-embedded-optional-read-operands-in-control-expression-programs.md`
- `docs/rules/unified-bytecode-prototypes.md`
