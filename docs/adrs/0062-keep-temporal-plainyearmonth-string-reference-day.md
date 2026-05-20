# ADR 0062: Keep Temporal PlainYearMonth string reference day

## Status

Accepted

## Context

Issue #851 / PR #1178 fixed the focused Test262
`Temporal_PlainYearMonth_prototype_equals` failures for
`intl402/Temporal/PlainYearMonth/prototype/equals/canonicalize-calendar.js`.

`Temporal.PlainYearMonth.prototype.equals` converts its argument through
`ToTemporalPlainYearMonth` before comparing year, month, reference day, and the
canonicalized calendar. Full-date string arguments such as
`2024-06-08[u-ca=islamicc]` already carry the ISO reference day that equality
must compare against an existing PlainYearMonth.

The previous string path canonicalized the calendar annotation and then
recomputed the reference day with `GetTemporalReferenceISODay`. For the
`islamicc` alias, canonicalization changes the calendar spelling to
`islamic-civil`; recomputing the reference day after that step changed the
comparison basis and made `equals` reject a calendar-equivalent full-date
string. Property-bag conversion still needs the calendar-date conversion path,
but a parsed full-date string already supplied the ISO reference day.

## Decision

Keep `ToTemporalPlainYearMonth` full-date string conversion on the parsed ISO
day after calendar annotation validation and canonicalization.

For future PlainYearMonth conversion/equality work:

1. treat string input reference day as parsed ISO data, not as a value to
   recompute after calendar alias canonicalization;
2. keep property-bag calendar conversion separate, because property bags may
   provide calendar-visible fields that require calendar-to-ISO validation;
3. preserve constructor-only strict calendar validation when adjusting shared
   calendar helpers;
4. compare calendars only after canonicalization, but compare reference day in
   the same domain that created the PlainYearMonth value; and
5. prove this class with the focused
   `Name=Temporal_PlainYearMonth_prototype_equals` Test262 method group,
   starting with the `canonicalize-calendar.js` fixture.

## Consequences

- PlainYearMonth review needs to distinguish full-date string parsing from
  property-bag calendar-date conversion before changing reference-day handling.
- Calendar alias fixes must not silently recompute adjacent Temporal reference
  slots just because the calendar ID spelling was canonicalized.
- Future equality fixes should include both string and property-bag aliases in
  local coverage so canonicalization and reference-day semantics stay coupled
  only where the spec requires it.
- This ADR is caused by issue #851 / PR #1178 and complements ADR 0048, ADR
  0057, ADR 0059, ADR 0060, ADR 0061, and the root
  `.claude/rules/ecmascript-abstract-operations.md` rule for Temporal
  calendar-domain behavior.
