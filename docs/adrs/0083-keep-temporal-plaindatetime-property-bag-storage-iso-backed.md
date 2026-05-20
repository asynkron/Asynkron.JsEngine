# ADR 0083: Keep Temporal PlainDateTime property-bag storage ISO-backed

## Status

Accepted

## Context

Issue #1074 / PR #1308 fixed
`Temporal.PlainDateTime.prototype.since` for non-ISO property-bag operands.
The conversion path already normalized the source calendar date and returned
both the calendar fields and the corresponding ISO date, but
`ApplyOverflowToDateTime` stored the calendar-space `year`, `month`, and `day`
back into `JsTemporalPlainDateTime`. Difference arithmetic then treated those
calendar-space fields as if they were ISO coordinates, producing the wrong
date relationship.

Earlier ADR 0048 correctly required non-ISO Temporal objects to expose
calendar-visible fields, but its storage wording was too broad for the current
runtime model. Current `PlainDateTime` getters derive visible calendar fields
through `GetPlainDateTimeCalendarFields(...)` over ISO-backed storage. That
means the object can expose non-ISO fields while still storing the ISO
projection needed by date arithmetic, range checks, and shared PlainDateTime
operations.

The delivery also added a focused regression for the adjacent property-bag
numeric conversion edge: `year: Infinity` must throw `RangeError` after the
observable `valueOf` lookup and call have happened.

## Decision

Keep `Temporal.PlainDateTime` property-bag construction on ISO-backed internal
storage after non-ISO calendar conversion. Calendar-visible fields remain an
observable getter/projection responsibility, not proof that source calendar
fields should be stored directly in the `JsTemporalPlainDateTime` date slots.

For future `Temporal.PlainDateTime` property-bag and difference work:

1. separate the source calendar fields, overflow-normalized calendar fields,
   and ISO storage projection in helper names and tests;
2. construct the runtime `JsTemporalPlainDateTime` with ISO date coordinates
   when a non-ISO calendar date has been converted;
3. preserve observable calendar fields through the existing calendar-field
   projection helpers;
4. keep finite-number validation after normal JavaScript coercion so
   `valueOf`, `toString`, and accessor order remain observable before
   `RangeError`; and
5. prove this class with the focused
   `Name=Temporal_PlainDateTime_prototype_since` Test262 group plus local
   coverage for both non-ISO calendar operands and Infinity property-bag
   fields.

## Consequences

- This ADR clarifies the storage-specific part of ADR 0048. ADR 0048 remains
  valid for observable calendar fields and era handling, but future work should
  not read it as permission to store calendar-space fields in ISO-backed runtime
  date slots.
- Review should check both sides of the Temporal object model: visible
  calendar-field projection and internal ISO-coordinate arithmetic.
- The caused-by incident is issue #1074 / PR #1308.

