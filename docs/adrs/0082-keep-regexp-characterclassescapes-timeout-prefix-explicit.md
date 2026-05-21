# ADR 0082: Keep RegExp CharacterClassEscapes timeout prefix explicit

## Status

Accepted

## Context

Issue #1058 came from the 2026-05-19 Test262 runner summary for
`RegExp_CharacterClassEscapes`. The report listed eight crashed rows in
`built-ins/RegExp/CharacterClassEscapes/...`, covering digit, non-digit,
non-whitespace, and non-word character-class escape fixtures in strict and
sloppy modes.

Investigation identified two possible owners: the dense RegExp runtime bridge
for class escapes and the Test262 harness timeout policy. The affected upstream
fixtures are generated from large Unicode ranges through `regExpUtils.js`; each
file exercises legacy, `u`, and `v` variants over large candidate strings. The
delivery did not change `JsRegExp` or generated Test262 data. PR #1289 instead
extended the existing Test262 execution-timeout helper to cover the
`built-ins/RegExp/CharacterClassEscapes/` fixture directory, while preserving
the default timeout for ordinary fixtures.

This is intentionally broader than the earlier file-specific timeout precedent
from ADR 0007. Review required the missing acceptance proof before closeout:
the final build re-entry ran
`dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "Name=RegExp_CharacterClassEscapes"`
and recorded 24 passing tests in 17.6 seconds with no crash or timeout.

## Decision

Keep the RegExp `CharacterClassEscapes` timeout override as an explicit
Test262 harness-policy exception, not as a RegExp runtime repair.

Directory-prefix timeout policy is allowed only when all of these are true:

- the upstream fixture family is a generated heavyweight pack with a shared
  semantic root;
- the issue evidence points at harness execution limits rather than incorrect
  ECMAScript RegExp behavior;
- the helper normalizes the optional leading `test/` root before matching;
- regression coverage proves at least one bare path and one `test/`-prefixed
  path from the affected family use the extended timeout;
- nearby ordinary fixtures keep the default timeout; and
- the issue-owned focused Test262 method group or exact affected fixtures pass
  after the harness-policy change.

For issue #1058, the accepted prefix is exactly
`built-ins/RegExp/CharacterClassEscapes/`, with a 90 second execution timeout.

## Consequences

- Future RegExp CharacterClassEscapes failures must still distinguish harness
  limits from actual `JsRegExp` syntax, matching, capture, `u`, or `v` behavior
  before changing runtime code.
- Prefix-based timeout overrides remain exceptional. Do not generalize this to
  arbitrary Test262 directories without a new proof and ADR.
- Review-stage proof gaps should route back to build for the exact affected
  method-group or fixture proof before learn records the decision.
- This ADR is caused by issue #1058 / PR #1289 and complements ADR 0007 plus
  `.claude/rules/test262-harness-policy.md`.
