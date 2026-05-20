# ADR 0058: Keep Temporal PlainDate difference calendar-unit arithmetic owned

## Status

Accepted

## Context

Issue #842 / PR #1163 fixed the focused Test262
`Temporal_PlainDate_prototype_until` failure for
`intl402/Temporal/PlainDate/prototype/until/until-across-lunisolar-leap-months.js`.

`Temporal.PlainDate.prototype.until` compares two PlainDate values with matching
calendars. The previous implementation accepted non-ISO calendars but still
computed month and year differences by projecting both endpoints onto ISO date
fields. That worked for ISO-shaped calendars, but it lost lunisolar leap-month
structure: Chinese and Dangi calendar years can contain leap months, and the
observable month count between two calendar dates is not always the ISO month
delta between their stored ISO projections.

The repair reused the existing BCL-backed calendar helpers to expose
calendar-visible PlainDate parts, resolve non-ISO `monthCode` values for
property bags, and compute `until` month/year largest-unit differences in
calendar space. Day and week largest-unit behavior still uses the elapsed ISO
date difference because those units are duration counts over stored ISO dates.

## Decision

Keep `Temporal.PlainDate.prototype.until` and adjacent PlainDate difference work
as a calendar-unit operation when the largest unit is `month` or `year` and the
matching non-ISO calendar is backed by the BCL helper layer.

For future PlainDate difference work:

1. do not route matching non-ISO calendars through ISO month/year arithmetic
   unless the calendar has first been proven ISO-shaped for that operation;
2. convert stored ISO endpoints to calendar-visible year, month, monthCode, and
   day before computing month or year largest-unit differences;
3. keep day and week largest-unit behavior on elapsed ISO dates unless the spec
   path being implemented explicitly requires calendar-unit balancing;
4. resolve PlainDate property-bag `monthCode` through the receiver/resolved
   calendar and target calendar year for BCL-backed calendars, especially
   leap-month codes; and
5. prove this class with the focused
   `Name=Temporal_PlainDate_prototype_until` Test262 method group plus local
   regression coverage that crosses a lunisolar leap month.

## Consequences

- PlainDate difference review needs to distinguish calendar-visible fields from
  internal ISO storage, just like PlainDate and PlainDateTime property-bag work.
- BCL calendar range limits remain the boundary for this supported non-ISO
  branch; unsupported or out-of-range calendars should keep existing explicit
  RangeError behavior instead of silently falling back to ISO month arithmetic.
- This ADR is caused by issue #842 / PR #1163 and complements ADR 0057 plus the
  root `.claude/rules/ecmascript-abstract-operations.md` rule for Temporal
  calendar-dependent abstract operations.
