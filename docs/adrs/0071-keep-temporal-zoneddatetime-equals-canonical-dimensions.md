# 0071. Keep Temporal ZonedDateTime equals on canonical instant + zone + calendar

## Status

Accepted.

## Context

`Temporal.ZonedDateTime.prototype.equals` is the observable surface for
ZonedDateTime equality. It coerces its argument through `ToTemporalZonedDateTime`
and then compares three dimensions: the underlying epoch instant, the time-zone
identity, and the calendar identity. Test262's
`Temporal_ZonedDateTime_prototype_equals` group exercises both the conversion
ordering and the canonical-identifier comparison.

Prior to issue #858 / PR #1196, three independent drifts were observable:

1. The property-bag path inside `ZonedDateTimePropertyBagToRecord` always probed
   the receiver for `era` and `eraYear`, even for calendars without era support.
   This violated the observable absent-property ordering rule that ADR 0046
   already established for `Temporal.ZonedDateTime.compare`.
2. `JsTemporalZonedDateTime.GetHashCode` only combined `Instant` and the raw
   `TimeZoneId`. `Equals` already canonicalized time-zone and calendar, so two
   instances that compared equal could hash differently — for example
   `Etc/UTC` vs `UTC`, or `+00:00` vs `UTC` — which silently corrupts hash-based
   containers seeded from ZonedDateTime keys.
3. The shared `CanonicalizeTimeZoneIdForComparison` helper depended on
   construction-time `ValidateTimeZoneIdentifier` to normalize casing. Any call
   site that handed it a raw mixed-case zone such as `europe/paris` would
   compare unequal to `Europe/Paris` even though both refer to the same IANA
   zone.

In addition, the IANA alias table mapped `Atlantic/Jan_Mayen` to
`Europe/Berlin`. The current upstream alias is `Arctic/Longyearbyen`, which is
what `equals` and `compare` canonicalization should observe.

## Decision

Keep ZonedDateTime equality on three canonical dimensions, with each piece
owned by a self-sufficient helper:

1. **Three-dimension equality.** `JsTemporalZonedDateTime.Equals` and
   `GetHashCode` must combine the epoch `Instant`, the result of
   `TemporalHelper.CanonicalizeTimeZoneIdForComparison(TimeZoneId)`, and the
   result of `TemporalHelper.CanonicalizeCalendarIdForComparison(Calendar)`.
   The hash code must mirror the equality comparison; never hash a strict
   subset of the equality fields.
2. **Self-sufficient time-zone canonicalization.**
   `CanonicalizeTimeZoneIdForComparison` must handle, in order: empty input,
   offset normalization (with `+00:00` collapsing to `UTC`), case-insensitive
   `UTC` matching, supported IANA-name resolution through
   `IntlUtilities.TryGetSupportedTimeZoneIdentifier` (which uses the
   `OrdinalIgnoreCase` `Lookup` dict and the `Ordinal` `Members` set), and
   alias resolution through `IntlUtilities.TryCanonicalizeTimeZoneAlias`. Do
   not rely on construction-time `NormalizeTimeZone` to fix casing for this
   helper.
3. **Era reads stay calendar-dependent.** Inside
   `ZonedDateTimePropertyBagToRecord`, gate the `era` and `eraYear` getter
   reads behind `CalendarUsesEras(calendarId)` so ISO-only calendars do not
   observe synthetic missing-property probes. Era-capable calendars still
   coerce and validate ordinary own era fields when present. This applies
   ADR 0046's property-bag observability rule to the `equals` argument
   conversion (which routes through `ToTemporalZonedDateTime`).
4. **Alias data follows current IANA.** Keep
   `IntlUtilities.TimeZoneAliasMap` aligned with current upstream IANA aliases
   when the alias is observable through equality. Specifically,
   `Atlantic/Jan_Mayen` resolves to `Arctic/Longyearbyen`, not `Europe/Berlin`.

## Consequences

- Pro: `equals` and hashing agree on the canonical equivalence classes for
  time-zone (`UTC` == `Etc/UTC` == `+00:00`) and calendar (`gregory` vs
  `iso8601` are distinct).
- Pro: The comparison helper survives future call sites that bypass
  `ValidateTimeZoneIdentifier`; case variants resolve to the canonical form.
- Pro: Property-bag observability stays consistent across `compare`, `equals`,
  and other `ToTemporalZonedDateTime` callers — ISO-only calendars do not
  observe era probes.
- Con: Three callers must stay aligned: `Equals`, `GetHashCode`, and any
  diagnostic that prints the comparison dimensions. A future refactor that
  adds a fourth equality dimension (for example, calendar variant) must update
  both `Equals` and `GetHashCode` in the same change.
- Con: Future IANA alias updates may require touching `TimeZoneAliasMap`
  again; treat alias drift as an observable equality input.

## Proof

- Three focused regressions added in `tests/Asynkron.JsEngine.Tests/TemporalTests.cs`:
  - `Temporal_ZonedDateTime_Equals_TimeZoneAliases`
  - `Temporal_ZonedDateTime_Equals_CalendarMatters`
  - `CanonicalizeTimeZoneIdForComparison_CaseVariants`
- Narrow Test262 proof: `Name=Temporal_ZonedDateTime_prototype_equals` reports
  130 passing tests on both Built-Ins and Intl402 lanes.
- Internal `Temporal_ZonedDateTime*` filter passed (89 tests, including the
  five `since`/`until` regressions merged in from origin/main).
- Implementation diff is bounded to
  `src/Asynkron.JsEngine/JsTypes/JsTemporalZonedDateTime.cs`,
  `src/Asynkron.JsEngine/StdLib/Intl/IntlUtilities.cs`, and
  `src/Asynkron.JsEngine/StdLib/Temporal/TemporalHelper.cs`.

## Related

- Issue #858, PR #1196.
- ADR 0046 — Temporal property-bag observability. This ADR extends rule 32 of
  that family from `compare` to the `equals` conversion path via
  `ToTemporalZonedDateTime`.
- ADR 0070 — Keep Temporal ZonedDateTime since/until step order explicit. The
  same `CanonicalizeTimeZoneIdForComparison` helper is the single source of
  truth for both equality and cross-zone difference checks; both ADRs require
  it to be self-sufficient.
