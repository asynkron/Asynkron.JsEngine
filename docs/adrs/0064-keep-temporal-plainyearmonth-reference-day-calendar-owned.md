# ADR 0064: Keep Temporal PlainYearMonth reference day calendar-owned

## Status

Accepted

## Context

Issue #850 / PR #1181 fixed the focused Test262
`Temporal_PlainYearMonth_from` failures for non-ISO calendar property bags,
era canonicalization, and reference-day selection.

`Temporal.PlainYearMonth` is not just an ISO year/month pair. For non-ISO
calendars, the object stores an ISO reference date that anchors the calendar
year/month, while the observable `year`, `month`, and `monthCode` fields must
come from the receiver calendar. The previous implementation mixed those
domains: getters and string formatting could expose or construct from the ISO
projection as if it were the calendar-visible year/month, and non-ISO BCE
Gregorian formatting could crash by forcing the reference date through host
`DateTime`.

The delivery also exposed that BCL calendar helpers are a support boundary, not
the source of all Temporal semantics. BCL month codes for unsupported
non-ISO leap months must be rejected rather than silently normalized, Hebrew
month-code mapping still needs its existing calendar-specific rules, and a
small set of known Chinese historical leap-month reference cases must remain
owned by the Temporal helper because they fall outside the supported .NET
Chinese calendar range.

## Decision

Keep `Temporal.PlainYearMonth.from` calendar conversion as a calendar-visible
field operation anchored by an ISO reference date.

For future `Temporal.PlainYearMonth.from`, getter, and string-formatting work:

1. map stored ISO reference dates back through the receiver calendar before
   exposing `year`, `month`, and `monthCode`;
2. use the stored ISO reference date directly for non-ISO string forms that
   require a date, instead of reconstructing BCE or out-of-range values through
   host `DateTime`;
3. treat `era` and `eraYear` as calendar-dependent property-bag fields, matching
   the neighboring Temporal field readers;
4. resolve `monthCode` through the resolved calendar and target calendar year,
   rejecting unsupported BCL leap-month codes while preserving known
   calendar-specific mappings such as Hebrew; and
5. keep explicit Temporal-owned reference-day cases when ECMA-402/Test262 covers
   valid calendar dates outside the host calendar range.

## Consequences

- PlainYearMonth review needs to distinguish calendar-visible year/month fields,
  stored ISO reference dates, and host calendar support boundaries.
- Host BCL calendars remain implementation helpers. They do not get to define
  every ECMA-402-valid reference day or silently normalize leap-month month
  codes.
- Future work should prove this class with the focused
  `Name=Temporal_PlainYearMonth_from` Test262 method group, plus targeted local
  coverage when a change touches getters or string formatting.
- This ADR is caused by issue #850 / PR #1181 and complements ADR 0048, ADR
  0057, ADR 0058, ADR 0060, ADR 0061, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for Temporal
  calendar-domain behavior.
