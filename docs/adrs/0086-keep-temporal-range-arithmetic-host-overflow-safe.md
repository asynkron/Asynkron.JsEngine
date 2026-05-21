# ADR 0086: Keep Temporal range arithmetic host-overflow safe

## Status

Accepted

## Context

Issue #1340 / PR #1350 fixed a broad Test262 crash bucket covering
`Temporal.PlainMonthDay.prototype.toPlainDate`,
`Temporal.PlainDateTime.prototype.add`, and `Temporal.Duration.from`.

The failures had different entry points, but they shared the same class of
runtime risk: Temporal accepts values at a different range and precision than
the host primitives used inside the implementation. Open-coded conversion could
truncate a huge `year` to `int`, `BigInteger` construction from non-finite
`double` values could throw host exceptions, and month normalization loops could
turn large duration values into excessive work instead of bounded arithmetic.

The delivery kept the fixes at the Temporal helper/runtime boundary:

- `PlainMonthDay.prototype.toPlainDate` now routes `year` through the shared
  Temporal integer/range validation path and validates the resulting ISO date
  before wrapping a `PlainDate`.
- Duration validity now checks that each numeric component is finite, integral,
  and representable before constructing `BigInteger` values for normalized
  nanosecond arithmetic.
- `PlainDate` month arithmetic now uses constant-time floor division instead of
  repeated month-normalization loops.

## Decision

Keep Temporal range arithmetic host-overflow safe. Temporal built-ins should
normalize and validate at the spec helper boundary before constructing host date
types, casting to narrow integers, or entering normalization loops whose runtime
depends on untrusted duration magnitudes.

For future Temporal range/arithmetic work:

1. route year, date, duration, and option numeric fields through the shared
   Temporal conversion helpers before narrowing to host integer types;
2. convert host `OverflowException`, `ArgumentOutOfRangeException`, or
   non-finite numeric input into JavaScript `RangeError` or `TypeError` at the
   Temporal operation boundary, not after a CLR exception escapes;
3. use `BigInteger` only after proving the source value is finite, integral,
   and representable for the intended construction path;
4. use constant-time calendar/date arithmetic for untrusted duration magnitudes
   instead of loops proportional to years, months, days, or subsecond totals;
   and
5. prove this class with focused internal regressions plus the owning Test262
   method groups for the touched Temporal operation.

## Consequences

- Review for Temporal crash-bucket fixes should check host bridge boundaries,
  not only spec-visible method names. A passing happy path can still leave a
  CLR crash or slow normalization path for extreme but valid Test262 inputs.
- This rule complements ADR 0044 for exact Temporal duration subsecond
  formatting and ADR 0051 for signed `Temporal.Duration.prototype.total`
  calendar-unit fractions. ADR 0086 covers validation and bounded arithmetic
  before those operation-specific paths run.
- The caused-by incident is issue #1340 / PR #1350.
