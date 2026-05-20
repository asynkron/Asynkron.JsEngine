# ADR 0040: Keep RegExp property escape astral patterns compact

## Status

Accepted

## Context

Issue #821 / PR #1114 fixed Test262
`RegExp_propertyEscapes_generated` crashes for large Unicode property escape
fixtures such as `General_Category_-_Letter.js`, `ID_Start.js`, and
`XID_Continue.js`.

The failure was not a Unicode data-generation bug. The runtime already had the
right range data and already avoided per-code-point expansion for astral
ranges, but it still emitted repeated surrogate-pair alternatives for every
high-surrogate segment. Large generated properties could therefore build very
large .NET regex patterns and time out during the Test262 generated property
escape proof.

The delivery compacted `BuildSurrogatePairRanges` in `JsRegExp.cs` by grouping
high surrogates that share the same normalized low-surrogate character class.
The resulting pattern preserves the same range semantics while avoiding
repeated low-surrogate classes across adjacent high-surrogate alternatives.
No generated Unicode data files were changed.

## Decision

Keep RegExp Unicode property escape runtime patterns compact in the
surrogate-pair encoder. For astral property ranges:

1. split input code point ranges into high-surrogate to low-surrogate ranges;
2. normalize low-surrogate ranges per high surrogate;
3. group high surrogates by identical normalized low-surrogate class text; and
4. emit one high-surrogate class per low-surrogate class instead of repeating
   the same low-surrogate class across many alternatives.

Future fixes for large property-escape crashes should first determine whether
the defect is data semantics, parser behavior, or runtime pattern size. When
range data is correct and the problem is .NET regex pattern size, keep the fix
in the runtime pattern builder rather than adding Test262 timeouts, editing
generated data, or broadening the harness.

## Consequences

- Large astral-heavy property escapes remain data-driven and range-based while
  producing materially smaller .NET regex patterns.
- The generated Unicode property table remains a derivative artifact; future
  agents should not edit it to work around runtime pattern-size failures.
- Negated property escapes must continue to preserve complement and lone
  surrogate behavior when using the compact surrogate-pair output.
- Focused proof for this boundary should include the exact failing fixture first
  and then the `Name=RegExp_propertyEscapes_generated` Test262 method group.
- This ADR complements
  `.claude/rules/ecmascript-regexp-unicode-properties.md`, especially the
  issue #821 guidance about distinguishing data-generation defects from runtime
  pattern-size defects.
