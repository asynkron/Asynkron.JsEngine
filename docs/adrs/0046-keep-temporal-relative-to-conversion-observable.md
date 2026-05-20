# ADR 0046: Keep Temporal relativeTo conversion observable

## Status

Accepted

## Context

Issue #832 / PR #1128 fixed the Test262
`BuiltInsTests.Temporal_Duration_compare` failures for
`built-ins/Temporal/Duration/compare/order-of-operations.js` and
`built-ins/Temporal/Duration/compare/relativeto-string-limits.js`.

The first delivery removed `era` and `eraYear` reads from
`ToRelativeTemporalObject` so ISO property bags matched the Test262 observable
property access order. Review then caught the missing half of that decision:
non-ISO, era-capable calendars still need `era` and `eraYear` read and coerced
at their alphabetical property slots before `hour`. The repair resolved the
calendar first, then conditionally read and converted `era` and `eraYear` only
when the resolved calendar uses eras. That kept ISO property bags from
observing missing `era` properties while preserving the required observable
operations for era-capable calendars.

The same issue also fixed fixed-offset `relativeTo` ZonedDateTime strings near
Temporal's representable range boundary. The parser now validates the explicit
offset against a fixed-offset time zone and returns without converting the
boundary instant through host `DateTimeOffset`, avoiding host-range overflow
while preserving offset mismatch rejection.

## Decision

Keep Temporal `relativeTo` conversion as an observable abstract-operation
sequence, not as a generic property-bag normalization pass.

For future Temporal `relativeTo` work:

1. resolve the property-bag calendar at the `calendar` step before deciding
   calendar-specific reads;
2. read and coerce `era` and `eraYear` at their alphabetical slots only when
   the resolved calendar is era-capable;
3. do not read missing ISO-only `era` fields merely to share a generic property
   list with non-ISO calendars;
4. preserve fixed-offset ZonedDateTime string validation without routing valid
   boundary instants through host `DateTimeOffset`; and
5. prove this class with the focused `Name=Temporal_Duration_compare` Test262
   method group, including both strict and sloppy variants when the issue came
   from that group.

## Consequences

- Future Temporal property-bag code should split always-read fields from
  calendar-dependent fields instead of assuming one property list fits every
  calendar.
- Review should check both observable order and missing-property behavior; a
  fix can pass one side while regressing the other.
- Boundary string parsing should distinguish Temporal representable range from
  host date/time conversion range before using host date types.
- This ADR is caused by issue #832 / PR #1128 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule.
