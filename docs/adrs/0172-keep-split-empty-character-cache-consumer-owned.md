# ADR 0172: Keep split empty character cache consumer-owned

## Status

Accepted

## Context

Issue #2079 / PR #2097 reopened the residual allocation gap for both `json` and
`stringops` after prior owner-specific slices. The important profiling result
was that the two profiles did not share a dominant allocation owner:

```text
json memory profile: 51.36 MB total, led by JsonHelper parse/stringify storage
stringops memory profile: 11.73 MB total, led by split/consumer strings and rope nodes
```

The selected implementation therefore stayed in `StringPrototype.Split` instead
of changing JSON helpers, rope flattening, or generic addition. The narrow hot
source was empty-separator split materialization: for ASCII-heavy strings,
`split("")` created a fresh one-character CLR string for each UTF-16 code unit
before pushing the JavaScript string value into the result array.

Baseline and final allocation matrices were:

```text
baseline: json 2677ms / 890739.0KB; stringops 926ms / 85474.6KB
final:    json 1235ms / 892007.2KB; stringops 413ms / 76106.1KB
```

The post-change `stringops` memory profile total was 11.21 MB, and sampled
`String` allocation dropped from 3.80 MB to 2.87 MB.

## Decision

Keep empty-separator split character reuse owned by `StringPrototype.Split`:

1. `split("")` may reuse a small static cache for ASCII single-code-unit CLR
   strings while building the result array.
2. Non-ASCII code units and surrogate code units stay on the existing
   `char.ToString()` path unless a future profile proves they are the current
   owner and pins the UTF-16 code-unit semantics.
3. Do not turn this consumer materialization win into JSON, rope flattening, or
   generic addition changes.
4. Do not introduce a global JavaScript string intern table from this evidence;
   the accepted cache is private to the string split consumer and only removes
   repeated CLR one-character string allocation on the proven ASCII path.
5. Keep observable split behavior unchanged: `@@split` delegation,
   `RequireObjectCoercible`, `ToString`, limit handling, separator conversion,
   result ordering, and UTF-16 code-unit output remain on the existing paths.

## Consequences

- ASCII-heavy `split("")` workloads avoid repeated one-character string
  allocation during result materialization.
- JSON remains a `JsonHelper`-owned residual; a stringops fix must not be used
  as evidence for JSON helper changes.
- ADR 0163 remains the owner boundary for this class of follow-up: split/join
  materialization wins stay at the consumer instead of reopening rope or
  addition policy.
- Future `stringops` work should reprofile before widening the cache or moving
  it. Widening beyond ASCII needs focused tests for surrogate pairs, unpaired
  surrogates, and non-ASCII BMP code units.

## Evidence

- PR #2097 merged commit `af10109e0d030743c2ae2e18b8e4427f35a11d66`.
- Delivery commit `d7277646c59bc24b2a532dbf286b14627f8f0a48` changed only
  `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`.
- Focused tests passed:
  `String_LongConcatenation_ConsumersObserveFullString`,
  `String_Split_EmptySeparator`, and `String_Split_WithLimit`.
- `rtk git diff --check` passed in the delivery stage.

## Related

- `docs/adrs/0163-keep-stringops-follow-up-consumer-materialization-owned.md`
- `docs/adrs/0120-keep-string-append-rope-flattening-consumer-driven.md`
- `docs/adrs/0158-keep-json-default-property-and-quote-fast-paths-profile-owned.md`
- `.claude/rules/performance-profiling-guardrails.md`
- `src/Asynkron.JsEngine/StdLib/String/StringPrototype.cs`
