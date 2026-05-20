# 0069. Keep Temporal `.from` property bags round-trip with their own getter output

## Status

Accepted.

## Context

`Temporal` property accessors and the matching `.from(propertyBag)` readers
share a contract: any value the getter emits for a field must round-trip back
through the corresponding setter. Issue #857 / PR #1195 broke this on the
Gregorian era axis in two stages:

1. The era getter `GetTemporalEra` was canonicalized so that
   `ZonedDateTime.era` and `PlainDate.era` returned `"gregory"` /
   `"gregory-inverse"` instead of the legacy aliases `"ce"` / `"bce"`.
2. The property-bag reader `ResolveTemporalEraYear` still only accepted the
   legacy aliases `"ce" | "ad" | "bce" | "bc"`.

Engine-produced values therefore could not round-trip:

```javascript
const zdt = Temporal.ZonedDateTime.from(...);            // era = "gregory"
Temporal.ZonedDateTime.from({                            // RangeError
  calendar: "gregory",
  era: zdt.era,
  eraYear: zdt.eraYear,
  ...
});
```

The same delivery slice also touched two adjacent Temporal surfaces that
exposed the same shape problem, but for different field domains:

- `ZonedDateTime.from` for both string and property-bag input was routing wall
  times through the local-PlainDateTime path, which mishandled DST gaps and
  ambiguous times. The fix introduced a shared `ResolveWallTimeInstant` helper
  that honours `disambiguation: "compatible" | "earlier" | "later" | "reject"`
  and validates the resulting epoch against the shared Temporal instant range.
- `PlainDate.from` / `PlainDateTime.from` for non-ISO calendars were resolving
  `monthCode` strings through `ResolveISOMonthCode`, which is an ISO-domain
  helper. Non-ISO calendars must use the calendar-neutral
  `MonthCodeNumericValue` extractor when the calendar id is not `"iso8601"`.

## Decision

Treat the getter side and the property-bag side as one contract.

1. **Canonical-name round-trip.** Whenever a Temporal getter is migrated to a
   new canonical string form, update the matching property-bag reader to accept
   the canonical form in the same delivery slice. Legacy aliases stay
   accepted; the canonical form is added, not substituted.
2. **Shared wall-time disambiguation.** `ZonedDateTime.from` string and
   property-bag entry points share one helper that resolves wall-clock
   components against the resolved time zone, observes the requested
   disambiguation policy at DST gaps and ambiguities, and validates the
   resulting epoch nanoseconds against the shared Temporal instant range. The
   string path uses true start-of-day semantics only when the input string has
   no time portion (consistent with ADR 0056 for `PlainDate.toZonedDateTime`).
3. **Calendar-scoped monthCode resolution.** `ResolveISOMonthCode` is reserved
   for the ISO calendar. Non-ISO calendar field readers use the calendar-neutral
   `MonthCodeNumericValue` so non-ISO monthCodes can still be matched against
   numeric month input without leaking ISO-month-code semantics.

## Consequences

- Pro: engine-produced era strings, calendar names, and other Temporal getter
  outputs round-trip through `.from(propertyBag)` without ambient knowledge of
  legacy aliases.
- Pro: `ZonedDateTime.from` produces correct exact instants at DST forward and
  backward transitions, with the requested `disambiguation` semantics, from
  both string and property-bag input.
- Pro: Non-ISO calendar property bags accept `monthCode` strings whose numeric
  shape disagrees with ISO interpretation (relevant for lunisolar and Hebrew
  calendars).
- Con: Two extra short-form era literals (`"gregory"`, `"gregory-inverse"`) are
  now silent synonyms in property bags. Future canonical renames must extend
  this list, not replace it, to keep older engine outputs round-trippable.

## Proof

- Internal regressions in `tests/Asynkron.JsEngine.Tests/TemporalTests.cs`:
  - `Temporal_ZonedDateTime_GregoryEra_RoundTrip_CE`
  - `Temporal_ZonedDateTime_GregoryEra_RoundTrip_BCE`
  - `Temporal_PlainDate_GregoryEra_RoundTrip_BCE`
- Run-quality gate verification `857-1779273583492597000` passed before merge.

## Related

- Issue #857, PR #1195.
- ADR 0046 — Temporal property-bag observability (broader Temporal rule family).
- ADR 0048 / 0055 / 0067 — keep Temporal calendar-visible fields owned by the
  receiver calendar, not by stored ISO storage fields.
- ADR 0056 — `PlainDate.prototype.toZonedDateTime` start-of-day semantics for
  omitted `plainTime`, which mirrors the no-time-string branch of
  `ZonedDateTime.from` here.
- ADR 0068 — Temporal ZonedDateTime offset and time-only arithmetic on epoch
  nanoseconds (companion delivery for the same Temporal surface).
