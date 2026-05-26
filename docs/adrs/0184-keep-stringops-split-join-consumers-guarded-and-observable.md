# ADR 0184: Keep stringops split/join consumers guarded and observable

## Status

Accepted

## Context

Issue #2084 / PR #2127 continued ADR 0163's stringops consumer-materialization
work. The selected workload still exercised `split` and `join`, but the next
avoidable allocation was no longer a rope-flattening or generic-addition
problem.

The accepted delivery changed two consumer surfaces:

- `StringPrototype.Split` stopped materializing a full host `string[]` for the
  non-empty separator path and now pushes pieces into a pre-sized `JsArray`
  through a limit-aware incremental splitter.
- `Array.prototype.join` added a dense-own primitive-string fast path for
  ordinary `JsArray` receivers.

The join path needed one build-stage repair before merge. The first fast path
was too broad for side-effectful element coercion; the repaired guard requires
primitive JavaScript strings up front and falls back for objects so the generic
path can run `ToString` in order. The regression
`Array_Join_FallsBackWhenElementToStringHasSideEffects` pins the case where an
element `toString` deletes a later element and the later read must observe the
prototype value.

The review-stage selected-profile proof showed a retained allocation reduction
but noisy timing:

```text
PR no-build:   stringops  asynkron_ms=332  asynkron_kb=63751.8
Base no-build: stringops  asynkron_ms=382  asynkron_kb=76051.8
```

The earlier build-stage updates had focused semantic tests but did not report a
full before/after stringops row, so review had to reconstruct that evidence.
Issue #2150 follows through on that gap by requiring comparable selected-profile
rows and explicitly separating timing and allocation claims when timing is
noisy.

## Decision

Keep this follow-up split/join optimization consumer-owned and semantics-first:

1. `StringPrototype.Split` may avoid an intermediate host `string[]` only after
   the existing observable split setup is complete: `@@split`, receiver
   coercion, limit coercion, separator coercion, `lim == 0`, undefined
   separator, and empty-separator handling stay on their existing paths.
2. Non-empty separator splitting should append substrings directly into a
   capacity-only `JsArray` and stop at the specified limit without truncating a
   previously materialized host array.
3. `Array.prototype.join` may bypass per-index string-key property lookup only
   after receiver coercion, `length` read, separator `ToString`, and the
   zero-length branch have run in spec order.
4. The join fast path applies only to ordinary `JsArray` receivers with no
   custom indexed properties, length within the dense index range, every index
   present as an own element, and every element already tagged as a primitive
   JavaScript string.
5. The join fast path must not call element `ToString`, consult prototypes, or
   treat `null`/`undefined` as empty strings. Objects, holes, inherited values,
   sparse/custom descriptors, proxies, array-like receivers, non-string
   elements, and length/item mismatches stay on the generic observable path.
6. Performance reports for this class must include comparable selected-profile
   before/after rows when the issue acceptance asks for a reduction claim. If
   timing is noisy, report allocation and timing separately instead of implying
   a timing win.

## Consequences

- ADR 0163's boundary remains intact: split/join consumer work does not justify
  changing `JsRopeString`, generic addition, or slot compound-add routing.
- Future stringops work may extend consumer shortcuts only when it can prove the
  exact consumer owns the cost and the shortcut preserves abstract-operation
  order.
- Dense join optimizations are storage-boundary shortcuts, not alternate
  `Array.prototype.join` semantics. Any broader shape must add focused negative
  coverage for prototype, proxy, descriptor, and side-effectful coercion
  fallbacks.
- Build-stage summaries for performance issues should carry the selected
  profile rows, not leave review to rediscover whether acceptance criterion
  evidence exists.

## Related

- `docs/adrs/0163-keep-stringops-follow-up-consumer-materialization-owned.md`
- `docs/adrs/0172-keep-split-empty-character-cache-consumer-owned.md`
- `docs/performance/stringops-split-join-consumer-follow-through.md`
- `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`
- `src/Asynkron.JsEngine/StdLib/Array/ArrayPrototype.Transformations.cs`
- `.claude/rules/performance-profiling-guardrails.md`
