# ADR 0160: Keep JSON compact key quoting append-shared

## Status

Accepted

## Context

Issue #2041 / PR #2048 continued the bounded `json` profile work after ADR
0158. The delivery targeted one remaining compact `JSON.stringify` cost in
`JsonHelper`: object-key emission still allocated a quoted key string before
appending it to the already-active compact object `StringBuilder`.

The selected profile command was:

```bash
rtk ./tools/profile json --cpu --calltree-depth 40 --calltree-width 40
```

The build-stage evidence reported these timing excerpts:

```text
baseline: real 17.11, user 6.06, sys 0.93
final:    real 12.11, user 3.76, sys 0.61
```

The accepted implementation added `AppendQuotedString(StringBuilder, string)`
and used it only for compact object-key emission. Keys that do not need JSON
escaping append quotes and content directly to the existing builder. Keys that
need escaping still delegate to `QuoteString`, preserving the existing escape
and surrogate behavior instead of cloning the escaping loop.

Focused JSON guardrails covered `JSON.parse` `__proto__` own-data-property
behavior and `JSON.stringify` quote, backslash, control-character, valid
surrogate-pair, and lone-surrogate behavior. The focused validation command was:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "Name~JSON_"
```

Result: passed with existing unrelated warnings.

## Decision

Keep compact JSON object-key quote allocation removal append-shared and
escape-shared.

Future JSON key-quoting work should:

1. append directly to the active compact object `StringBuilder` only on the
   no-gap compact object path;
2. call the shared `QuoteString` path for any key that contains a quote,
   backslash, control character, or surrogate code unit;
3. avoid duplicating JSON escape or surrogate handling in a second loop unless
   that loop is proven equivalent and pinned by focused tests;
4. keep property enumeration and value serialization on the existing semantic
   paths before writing the key, so skipped properties, replacers, `toJSON`,
   proxies, raw JSON, circular checks, and pretty-printing behavior stay
   observable-equivalent; and
5. pair any further key-quoting widening with focused escaping/surrogate tests
   and a current selected-profile baseline/final comparison.

## Consequences

- The compact object path avoids an intermediate quoted-key string for common
  object keys without introducing a second escaping implementation.
- Escaping-required keys remain on the same `QuoteString` semantics as the rest
  of `JSON.stringify`.
- Future `json` profile slices should treat this as a continuation of ADR 0158:
  remove avoidable intermediate work only when the owning JSON algorithm step
  and the observable fallback boundary are explicit.

## Related

- `docs/adrs/0158-keep-json-default-property-and-quote-fast-paths-profile-owned.md`
- `docs/performance/json-default-data-properties-and-quote-fast-path.md`
- `docs/performance/json-compact-serialization.md`
- `.claude/rules/performance-profiling-guardrails.md`
