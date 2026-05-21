# ADR 0092: Keep anchored RegExp property escape construction pre-normalization

## Status

Accepted

## Context

Issue #1377 / PR #1384 fixed slow generated Test262
`RegExp_propertyEscapes_generated` fixtures for anchored Unicode property
escape patterns. The affected shapes were exact full-string checks such as
`/^\p{Script_Extensions=Arabic}+$/u` and their negated `\P{...}` variants.

ADR 0088 already established that this exact shape belongs to a narrow
`JsRegExp` runtime matcher instead of generated Unicode data or Test262 timeout
policy. The remaining issue was construction order. `JsRegExp` still normalized
the pattern through the .NET regex bridge before it created and selected the
anchored property matcher. For large Unicode property ranges, that meant
expanding the property escape into a large .NET regex pattern and paying compile
or normalization cost even though the runtime matcher would own all later
matching.

The delivery moved anchored property matcher creation ahead of
`NormalizePattern(...)` and returns early when the matcher accepts the original
ECMAScript pattern and flags. The proof also split Unicode data warm-up from
regex compile, sample construction, and match timing in `PropertyEscapeProfile`
so future measurements can distinguish one-time Unicode resolver initialization
from regex construction and match-time costs.

## Decision

Construct the exact anchored Unicode property escape matcher before RegExp
normalization and .NET regex construction.

When `TryCreateAnchoredPropertyEscapeMatcher(...)` accepts a pattern:

1. preserve the original ECMAScript source as `_normalizedPattern`;
2. keep `_regexOptions` at `RegexOptions.CultureInvariant`;
3. leave group metadata maps null because the accepted grammar has no captures;
4. define `lastIndex` normally for fresh RegExp objects before returning; and
5. skip `NormalizePattern(...)`, duplicate group renaming, capture reset map
   construction, and initial .NET `Regex` construction entirely.

The accepted grammar remains the ADR 0088 grammar: `u`-only, non-global,
non-sticky, exact `^\p{...}+$` or `^\P{...}+$`, whole-input, capture-free, and
declining every mixed or observable RegExp shape back to the normal bridge.

For future performance work in this family, profile and report the phases
separately: Unicode data warm-up, RegExp construction/compile, Test262-style
sample string construction, positive match, and negated match. Do not claim a
construction optimization from only match-time evidence.

## Consequences

- The anchored matcher is both the construction path and the execution path for
  its exact grammar, avoiding large generated .NET regex patterns for fixtures
  that do not need them.
- Unicode data warm-up remains visible as a separate cost; it should not be
  mistaken for per-RegExp construction time.
- Any future expansion of the accepted grammar must re-prove RegExp-visible
  behavior such as captures, `lastIndex`, sticky/global semantics, unicode sets,
  and legacy RegExp statics before moving it ahead of normalization.
- Focused proof should include internal RegExp tests for representative
  properties plus exact generated Test262 fixtures from the issue, including
  positive and negated forms.
- This ADR is caused by issue #1377 / PR #1384 and complements ADR 0088,
  ADR 0040, and `.claude/rules/ecmascript-regexp-unicode-properties.md`.
