# ADR 0077: Keep lexer identifier classification RegExp-data-free

## Status

Accepted

## Context

Issue #1040 / PR #1283 fixed the Test262 `Identifiers` crash group from the
2026-05-19 runner summary. The failing fixtures covered raw and escaped
supplementary-plane identifier starts such as
`language/identifiers/start-unicode-10.0.0.js` and
`language/identifiers/start-unicode-15.0.0-escaped.js`.

The delivery made `JsLexer` consume raw surrogate pairs atomically and validate
escaped identifier code points by position. A review pass then found a
hot-path regression in the new helper: normal identifier classification reused
`UnicodePropertyData.Resolve(...)`, pulling the RegExp Unicode property dataset
into ordinary parser tokenization.

That dependency was functionally convenient but architecturally wrong. RegExp
Unicode property escape data is large, generated, and runtime-oriented. Lexer
identifier checks are parser hot-path work and should stay lightweight,
allocation-stable, and independent of RegExp property resolver initialization.

## Decision

Keep ECMAScript lexer identifier classification in a parser-owned helper that
does not call `UnicodePropertyData.Resolve(...)` or otherwise initialize RegExp
Unicode property data.

For `ID_Start` and `ID_Continue`, classify the decoded scalar directly with
`Rune.GetUnicodeCategory(...)` for the ECMAScript category sets used by the
lexer. Preserve compatibility code points from `Other_ID_Start` and
`Other_ID_Continue` explicitly in the parser helper instead of resolving the
generated RegExp property tables at runtime.

When adding or repairing supplementary-plane identifier support, keep the proof
focused:

1. local parser/lexer regressions for raw supplementary starts, escaped starts,
   private identifiers, and invalid escaped start/continuation cases; and
2. the issue-owned Test262 method group
   `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=Identifiers" --logger "console;verbosity=minimal"`.

Do not edit `.generated.` Unicode property files for this class of bug. If a
future change needs more generated Unicode knowledge for identifiers, first
decide whether that data belongs in a small parser-owned generated artifact
rather than reusing RegExp property escape infrastructure.

## Consequences

- Parser identifier tokenization remains independent from heavyweight RegExp
  Unicode-property resolver initialization.
- RegExp Unicode property data continues to belong to property escapes and their
  generator/runtime bridge, not ordinary lexer hot paths.
- The parser helper carries a small explicit compatibility set for
  `Other_ID_Start` / `Other_ID_Continue`; changes to that set need focused
  identifier proof.
- Future review of identifier work should search the parser path for
  `UnicodePropertyData` and `Resolve(...)` references before accepting a fix.
- This ADR is caused by issue #1040 / PR #1283 and is enforced by
  `.claude/rules/ecmascript-regexp-unicode-properties.md`.
