# ADR 0113: Keep anchored RegExp property escape single-codepoint matching runtime-owned

## Status

Accepted

## Context

Issue #1743 / PR #1766 fixed generated Test262
`RegExp_propertyEscapes_generated` crashes and early terminations for exact
Unicode property escape fixtures such as
`built-ins/RegExp/property-escapes/generated/Extended_Pictographic.js`.

Earlier RegExp property-escape decisions already moved exact full-string
`/^\p{...}+$/u` and `/^\P{...}+$/u` matching into a narrow `JsRegExp` runtime
matcher and then moved matcher construction before RegExp normalization. Those
decisions covered one-or-more whole-input property checks, but the generated
fixture bucket also includes exact single-codepoint checks shaped as
`/^\p{...}$/u` and `/^\P{...}$/u`.

Falling back to the .NET regex bridge for those single-codepoint shapes would
reintroduce the same generated-property range expansion and matching costs that
ADR 0088 and ADR 0092 intentionally avoided. Treating the single-codepoint form
as equivalent to the `+` form would also be semantically wrong, because
`/^\p{Extended_Pictographic}$/u` must accept exactly one Unicode code point and
reject two matching code points.

## Decision

Keep exact anchored Unicode property escape single-codepoint matching in the
same narrow `JsRegExp` runtime matcher family as the existing one-or-more
whole-input matcher.

The accepted grammar is still intentionally small:

1. `u` flag only, with no global or sticky behavior;
2. exact source shapes `^\p{...}$`, `^\P{...}$`, `^\p{...}+$`, and
   `^\P{...}+$`;
3. no captures, alternation, mixed atoms, unicode sets, or surrounding pattern
   text;
4. Unicode property resolution through `UnicodePropertyData.Resolve(...)`; and
5. JavaScript string scanning by Unicode code point, including surrogate pairs
   and lone surrogate code units.

For the non-quantified form, the matcher must require exactly one consumed code
point. For the `+` form, it may continue to scan the whole string and require
every consumed code point to satisfy the property membership decision. Unsupported
or observable RegExp shapes must decline to the normal bridge rather than widen
the custom matcher by guesswork.

## Consequences

- Generated Unicode property escape fixtures can stay on the same runtime-owned
  fast path for exact one-codepoint and one-or-more full-string checks.
- The matcher grammar now has an explicit cardinality dimension; future changes
  must prove both one-codepoint and repeated-codepoint behavior.
- The internal proof should include an astral positive case and a same-property
  two-codepoint rejection for the non-quantified shape, because BMP-only tests
  can miss surrogate-pair cardinality mistakes.
- Broad Test262 timeout increases or generated Unicode data edits remain the
  wrong fix when the issue is this exact runtime matching shape.

## Related

- `docs/adrs/0040-keep-regexp-property-escape-astral-patterns-compact.md`
- `docs/adrs/0088-keep-anchored-regexp-property-escape-matching-runtime-owned.md`
- `docs/adrs/0092-keep-anchored-regexp-property-escape-construction-pre-normalization.md`
- `.claude/rules/ecmascript-regexp-unicode-properties.md`
