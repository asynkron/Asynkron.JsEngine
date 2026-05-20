# ADR 0034: Keep Intl DurationFormat fractional math exact

## Status

Accepted

## Context

Issue #1025 / PR #1097 fixed the Test262
`Intl402Tests.DurationFormat_prototype_format` failures for
`precision-exact-mathematical-values.js`. The failing fixture exposed that
`Intl.DurationFormat.prototype.format` cannot combine seconds, milliseconds,
microseconds, and nanoseconds through binary `double` arithmetic before number
formatting. ECMA-402 requires the fractional unit value to be the exact
mathematical combination of the relevant duration fields, and the binary
intermediate can introduce rounding artifacts before the formatter applies
truncation, padding, sign, and fractional digit behavior.

The repair kept the ownership local to `IntlDurationFormatPrototype`: duration
sub-second units are converted to an exact fractional decimal string, then
formatted through the Intl number formatter path that already owns exact decimal
lexeme formatting. That keeps the DurationFormat-specific unit combination
explicit without moving sub-second duration semantics into global numeric
conversion.

The same delivery also exposed quality-gate friction unrelated to DurationFormat
semantics. `AsyncAwaitTests.AsyncFunction_WithParallelDelays` used raw
`setTimeout` calls even though it was proving Promise.all ordering rather than
timer behavior. The canonical quality gate timed out twice before the test was
switched to the repository's tracked delay helper.

## Decision

Keep `Intl.DurationFormat` fractional unit combination exact until the value is
handed to the Intl number formatter.

For future DurationFormat work:

1. combine seconds, milliseconds, microseconds, and nanoseconds as exact decimal
   quantities before formatting;
2. avoid `double` addition for fractional unit aggregation, even when the source
   duration fields individually fit in a `double`;
3. preserve sign separately from the absolute exact magnitude so negative
   fractional values keep the existing sign-display behavior;
4. route the final decimal string through the Intl-owned number formatting path
   so truncation, padding, grouping, and parts behavior stay centralized; and
5. prove this class with focused internal DurationFormat regressions plus the
   exact `Name=DurationFormat_prototype_format` Test262 method group when the
   issue came from that cluster.

For async runtime tests that are not specifically testing timer behavior, use
the repository's tracked async delay helpers instead of raw `setTimeout` timers.
Timer scheduling should be the subject of a timer-specific test, not an
incidental dependency of an ordering assertion.

## Consequences

- Future DurationFormat fractional fixes should extend the exact decimal helper
  path in `IntlDurationFormatPrototype` or shared Intl number formatting, not
  reintroduce generic `double` conversion as a shortcut.
- Review should look for fallback paths where exact decimal formatting rejects
  or bypasses a value and silently returns to binary floating-point aggregation.
- Tests for DurationFormat fractional behavior should include values where a
  binary intermediate would round before the formatter applies truncation or
  fractional digit rules.
- Non-timer async tests should stay deterministic under the canonical quality
  gate by using `AsyncTestHelpers.RegisterDelayHelper`.
- This ADR is caused by issue #1025 / PR #1097 and complements
  `.claude/rules/ecmascript-numeric-coercions.md` for exact Intl numeric
  formatting boundaries.
