# ADR 0074: Keep Temporal PlainYearMonth.with calendar year-month fields

## Status

Accepted

## Context

Issue #854 / PR #1186 fixed the focused Test262
`Temporal_PlainYearMonth_prototype_with` failures for non-ISO calendar field
merging, calendar/timeZone rejection, era handling, and Hebrew leap-month
overflow behavior.

`Temporal.PlainYearMonth.prototype.with` applies partial year/month overrides
to the receiver. For non-ISO calendars, the receiver's observable `year`,
`month`, and `monthCode` are calendar fields, while the object also carries an
ISO reference day for internal completion. The previous implementation could
merge overrides against the stored ISO projection or resolve omitted
`monthCode` values too early. That made Hebrew year-only overrides interact
poorly with leap month `M05L`: default `overflow: "constrain"` must not throw
before options are read, while explicit `{ monthCode: "M05L" }` with
`overflow: "reject"` must remain a deterministic RangeError in a non-leap
target year.

The build retry for this issue also showed that inherited/defaulted
`monthCode` behavior and explicit override behavior must be tested separately.
The final regression made the reject path explicit with
`leap.with({ year: nonLeapYear, monthCode: "M05L" }, { overflow: "reject" })`
so the test asserts invalid explicit leap-month input rather than an ambiguous
defaulting path.

## Decision

Keep `Temporal.PlainYearMonth.prototype.with` as a receiver-calendar
year-month field merge before reference-day and internal ISO conversion.

For future `PlainYearMonth.prototype.with` and adjacent non-ISO calendar work:

1. default partial overrides from the receiver's observable calendar fields,
   not from the internal ISO reference projection;
2. reject override objects with `calendar` or `timeZone` before merging
   year-month fields;
3. read and merge `era`/`eraYear` only for era-capable calendars, and require
   them as a pair when used;
4. read `overflow` before resolving month/monthCode defaults so omitted Hebrew
   leap-month defaults can follow constrain/reject semantics at the
   CalendarYearMonthFromFields boundary;
5. preserve the receiver's visible `monthCode` only when no explicit `month`
   or `monthCode` override is supplied, and validate explicit leap
   `monthCode` overrides in the target calendar year; and
6. convert or validate through shared PlainYearMonth overflow/reference-day
   helpers only after the calendar-visible field merge is complete.

## Consequences

- PlainYearMonth review needs to distinguish observable calendar year/month
  fields, explicit override fields, inherited defaults, and the stored
  reference day before changing `.with`.
- Hebrew leap-month tests should keep constrain-default and explicit-reject
  cases separate; a passing year-only constrain test does not prove explicit
  leap `monthCode` rejection.
- Future work should prove this class with the focused
  `Name=Temporal_PlainYearMonth_prototype_with` Test262 method group plus
  targeted local coverage for Hebrew leap-month constrain/reject behavior.
- This ADR is caused by issue #854 / PR #1186 and complements ADR 0055, ADR
  0064, ADR 0067, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for observable
  Temporal calendar-field conversion.
