# ADR 0003: Defer Annex B single-escape RegExp construction

## Status

Accepted

## Context

Issue #757 tracked the Test262 `BuiltIns_RegExp` crash for
`annexB/built-ins/RegExp/RegExp-leading-escape-BMP.js`. The test builds many
short-lived Annex B patterns such as `/\a/` and checks that `.source` preserves
the legacy identity escape.

Before PR #891, `JsRegExp` eagerly constructed a .NET `Regex` during JavaScript
RegExp construction. Small patterns also used `RegexOptions.Compiled`. That was
reasonable for reusable regular expressions, but it created thousands of
compiled .NET regexes for the Test262 BMP escape loop even when the JavaScript
program only inspected `.source`.

RegExp construction cannot become generally lazy: JavaScript must still throw
syntax errors at construction time for invalid patterns, and named-group,
capture, and quantifier-reset metadata depend on normalized .NET regex state.

## Decision

Defer initial .NET `Regex` construction only for the narrow Annex B single
legacy identity escape shape:

- no flags,
- exactly two source characters,
- leading backslash,
- escaped character is not a line terminator.

All other `JsRegExp` instances keep construction-time `EnsureRegex()` behavior.
The deferred single-escape path also skips `RegexOptions.Compiled` until a match
actually needs the regex object.

## Consequences

- The Test262 BMP source-round-trip loop avoids compiled-regex churn while still
  preserving the `.source` identity escape requirement.
- Construction-time syntax errors remain eager outside this proven safe shape.
- Future RegExp construction optimizations should not widen laziness without a
  proof that invalid-pattern timing and capture metadata behavior remain
  ECMAScript-compatible.
- Focused proof for this boundary should include the `BuiltIns_RegExp` method
  group and the internal Annex B identity escape tests added for issue #757.
