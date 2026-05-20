# ADR 0044: Keep Temporal Duration subsecond formatting exact

## Status

Accepted

## Context

Issue #833 / PR #1129 fixed the Test262
`BuiltInsTests.Temporal_Duration_from` failures for
`argument-duration-precision-exact-numerical-values.js`. The failing fixture
uses object-bag duration fields near the `2**53` seconds boundary, with very
large millisecond and microsecond values that are still valid exact numerical
Temporal duration input.

The delivery kept the fix in `TemporalHelper.FormatDurationToString`. It did
not widen duration validation or change `JsTemporalDuration` storage. Instead,
it preserved millisecond, microsecond, and nanosecond magnitudes as
`BigInteger` while balancing subsecond units into seconds for string output.
That avoided lossy or overflowing `long` casts before the final bounded
subsecond result was produced.

This recurrence is close to ADR 0034 for `Intl.DurationFormat` fractional math,
but the owner is a different observable surface: `Temporal.Duration` ISO string
formatting and `Temporal.Duration.from(...).toString()`, not Intl number
formatting.

## Decision

Keep Temporal duration subsecond formatting exact until balancing has reduced
the value to the bounded output unit.

For future Temporal duration formatting and parsing work:

1. preserve millisecond, microsecond, and nanosecond magnitudes as exact integer
   quantities while aggregating or balancing them;
2. avoid casting large subsecond component magnitudes to `long` or aggregating
   them through `double` before balancing;
3. keep validation and storage changes separate from formatting fixes unless
   the failing behavior proves the validation or storage boundary is wrong;
4. preserve sign handling separately from absolute magnitude while formatting;
   and
5. prove this class with the focused `Name=Temporal_Duration_from` Test262
   method group, including both strict and sloppy variants when the issue came
   from that cluster.

## Consequences

- Future `Temporal.Duration` string-formatting fixes should extend the exact
  `BigInteger` balancing path in `TemporalHelper`, not reintroduce host integer
  casts before subsecond balancing.
- Review should inspect both rounding and no-rounding branches, because either
  branch can aggregate milliseconds, microseconds, and nanoseconds into a
  larger unit.
- This ADR is caused by issue #833 / PR #1129 and complements
  `.claude/rules/ecmascript-numeric-coercions.md` plus ADR 0034 for the related
  `Intl.DurationFormat` exact-fractional boundary.
