# ADR 0088: Keep anchored RegExp property escape matching runtime-owned

## Status

Accepted

## Context

Issue #1332 / PR #1346 fixed generated Test262
`RegExp_propertyEscapes_generated` crashes and timing failures for representative
fixtures such as `Alphabetic.js`, `Any.js`, and `ASCII_Hex_Digit.js`.

The Unicode property range data was already correct, and the prior property
escape compaction work had already reduced large pattern size. The remaining
problem was a narrower runtime shape: exact full-string checks of the form
`/^\p{...}+$/u` or `/^\P{...}+$/u` still paid the cost of building and running
a .NET `Regex` even though the ECMAScript-visible result is equivalent to a
linear code-point membership scan over the resolved Unicode ranges.

Review initially sent the delivery back to build because the representative
Test262 cases passed but took about 10.03s to 10.22s each in the local TRX,
over the explicit under-10s gate. The final build tightened the matcher by
short-circuiting all-codepoint ranges like `Any` and using a linear scan for
very small range sets like `ASCII_Hex_Digit`. The final representative Release
TRX recorded all six strict and non-strict cases passing at about 7.36s to
7.73s.

## Decision

Keep exact anchored Unicode property escape full-string matching in `JsRegExp`
runtime code, not in generated Unicode data and not in Test262 harness timeout
policy.

The fast path is intentionally narrow:

1. It applies only to exact `u`-only, non-global, non-sticky patterns shaped as
   `^\p{Property}+$/u` or `^\P{Property}+$/u`.
2. It resolves the property through `UnicodePropertyData.Resolve(...)` and uses
   the same range data as the normal property escape encoder.
3. It reads JavaScript strings by Unicode code point, including surrogate pairs
   and lone surrogate code units.
4. It updates whole-input `RegExp` statics and `exec` result shape explicitly,
   because there are no capture groups in this accepted fast-path grammar.
5. It declines all other patterns so the normal normalized .NET regex bridge
   remains responsible for captures, `lastIndex`, sticky/global behavior,
   unicode sets, quantifier variants, and mixed atoms.
6. It must prove both semantic behavior and timing behavior with focused
   internal tests plus the representative generated Test262 fixtures when the
   issue is performance-gated.

Do not generalize this into a broader RegExp parser or a replacement for
property escape pattern generation without new proof for every observable
RegExp feature that would move onto the custom path.

## Consequences

- The generated Unicode property table remains the source of data, while the
  runtime owns the matching strategy for this exact full-string shape.
- Broad Test262 timeouts remain the wrong fix for this class when a narrow
  runtime matcher can satisfy the explicit timing gate.
- Future RegExp property escape performance fixes should distinguish data
  generation, pattern encoding size, and match-time execution cost before
  choosing the owner.
- The matching proof should include both an all-codepoint property such as
  `Any` and a very small property such as `ASCII_Hex_Digit`, because they stress
  different matcher costs.
- This ADR complements ADR 0040 and
  `.claude/rules/ecmascript-regexp-unicode-properties.md`.
