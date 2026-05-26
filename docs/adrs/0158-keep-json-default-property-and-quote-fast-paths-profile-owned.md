# ADR 0158: Keep JSON default-property and quote fast paths profile-owned

## Status

Accepted

## Context

Issue `autrun-discqnniowqw-45a6add5dd` / PR #2018 continued the bounded
optimizer run against the `json` profile. The required benchmark matrix showed
`json` as a current high-gap synchronous profile:

```text
json                           8481     1973  Jint 4.30x faster
```

A repeated focused baseline before editing was:

```text
json                           4894     1883  Jint 2.60x faster
```

The CPU profile kept the owner surface inside `JsonHelper`: fresh JSON object
creation and reviver wrapper/context properties were paying full descriptor
allocation for default data properties, and compact string quoting still paid
the `StringBuilder` escape path for strings that required no escaping.

The accepted delivery reused `JsObject.DefineDefaultDataProperty` for JSON
objects that the parser creates fresh, for reviver root wrappers, and for
reviver source context objects. It also added a `QuoteString` fast path that
returns a directly quoted string only when the input contains no quote,
backslash, control character, or surrogate code unit.

Focused final timing improved the selected benchmark from the 4894 ms focused
baseline to 3799 ms, with warm direct Asynkron runs at 3745 ms and 3801 ms.

## Decision

Keep JSON parse/stringify performance shortcuts profile-owned and constrained to
the JSON semantics they can prove.

Future JSON default-property work should:

1. use `JsObject.DefineDefaultDataProperty` only when JSON is creating a fresh
   ordinary own data property whose observable descriptor is writable,
   enumerable, and configurable;
2. preserve `__proto__` as an own data property in JSON-created objects instead
   of invoking setter behavior;
3. keep existing objects, non-default descriptors, proxies, reviver mutation,
   and source-tracking metadata on their semantic fallback paths; and
4. pair any widening with focused JSON tests plus repeated selected-profile
   timing, because `json` timings are noisy.

Future string-quote work should:

1. skip the escape builder only for strings that contain no JSON escaping
   triggers and no surrogate code units;
2. leave control characters, quotes, backslashes, valid surrogate pairs, and
   unmatched surrogate handling on the existing escape path; and
3. keep replacer, `toJSON`, raw JSON, pretty-printing, proxy-aware key
   enumeration, circular checks, and reviver source tracking on their existing
   semantic paths.

## Consequences

- JSON parse can reuse the same storage-owned default-data-property boundary as
  object literals without turning that boundary into a generic descriptor
  bypass.
- Compact string quoting avoids avoidable builder work for common strings while
  preserving surrogate and escape correctness.
- Future `json` benchmark work should extend `JsonHelper` or the owning runtime
  storage helper only after the CPU profile identifies that exact owner.
- A performance improvement alone is not enough for JSON helpers; the fast path
  must be indistinguishable from the ECMAScript algorithm at observable
  property, escaping, reviver, and replacer boundaries.

## Related

- `docs/performance/json-default-data-properties-and-quote-fast-path.md`
- `docs/performance/json-compact-serialization.md`
- `docs/adrs/0106-keep-object-literal-default-data-properties-implicit.md`
- `.claude/rules/performance-profiling-guardrails.md`
