# ADR 0163: Keep stringops follow-up materialization consumer-owned

## Status

Accepted

## Context

Issue #2053 / PR #2067 followed ADR 0120's string append rope work. The goal was
to classify the remaining `stringops` cost before changing rope policy or
addition routing again.

The baseline selected-profile allocation run was:

```text
rtk ./benchmark.sh --allocations stringops
stringops  asynkron_ms=449  asynkron_kb=109942.9
```

The follow-up CPU and memory profiles no longer pointed at append-loop rope
flattening as the dominant owned cost. The visible owner was consumer-side
split/join work:

```text
CPU profile: Split 12.49 ms, Join 8.55 ms
Memory profile: 12.17 MB total, with CreateArrayFromStrings list growth under Split
```

That made a global rope or addition change the wrong layer. The narrow owner was
`StringPrototype.Split` result materialization, especially pre-sizing the
result `JsArray` and avoiding an intermediate `string[]` for empty-separator
splits.

## Decision

Keep this `stringops` follow-up consumer-owned:

1. Do not change `JsRopeString` flattening policy, generic addition, or slot
   compound-add routing when the current profile points at split/join consumer
   materialization.
2. `StringPrototype.Split` may materialize result arrays through pre-sized
   `JsArray` instances when the constructor is used as capacity-only and the
   elements are still appended in observable order.
3. Empty-separator split may avoid building a temporary `string[]`; create each
   one-character JavaScript string while pushing into the result array instead.
4. Keep ECMAScript order and delegation intact: `RequireObjectCoercible`,
   `@@split`, `ToString`, limit handling, separator conversion, and array
   indexing semantics stay on the existing paths.
5. Future `stringops` slices must reprofile before editing. The owner may shift
   between append-loop construction, consumer flattening/materialization, join,
   or another string conversion path.

## Consequences

- The accepted change stays in `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`.
- ADR 0120's consumer-driven flattening boundary remains intact; this ADR adds
  the follow-up rule that consumer materialization wins should stay at the
  consumer instead of being pushed back into rope storage or generic addition.
- The final stable allocation rerun moved from 109942.9 KB to 85462.7 KB.
- The final memory profile total moved from 12.17 MB to 10.94 MB.
- The final CPU profile showed `Split` at 4.40 ms and `Join` at 6.86 ms.
- Focused string consumer tests passed, including long concatenation consumers
  plus split empty-separator and limit coverage.

## Related

- `docs/adrs/0120-keep-string-append-rope-flattening-consumer-driven.md`
- `docs/performance/stringops-rope-append-fast-path.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`
