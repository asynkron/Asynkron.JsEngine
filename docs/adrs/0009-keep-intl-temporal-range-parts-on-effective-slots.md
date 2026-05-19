# ADR 0009: Keep Intl Temporal range parts on effective slots

## Status

Accepted

## Context

Issue #768 fixed the Test262
`DateTimeFormat_prototype_formatRangeToParts` failures for Temporal Plain*
operands. The failing cases covered option filtering, resolved time-zone
behavior, and mixed Temporal operand kinds.

The earlier `formatRange` repair for issue #767 had already established that
supported Temporal Plain* operands cannot be treated as ordinary
epoch-millisecond values. `formatRangeToParts` still had its own helper path:
it converted operands before deriving the Temporal effective slots and then
built source-tagged parts from the wrong formatter shape. That meant
`PlainDate`, `PlainTime`, `PlainDateTime`, `PlainYearMonth`, and
`PlainMonthDay` could observe date/time fields that should have been filtered
for their Temporal kind.

The same delivery also exposed a quality-gate friction point unrelated to Intl:
the async runtime tests are timer and event-loop sensitive, so running them in
the assembly-wide parallel pool made distinct timeout tests fail under load even
after the Intl fix was correct.

## Decision

For `Intl.DateTimeFormat` Temporal range helpers, supported same-kind Temporal
Plain* operands must be normalized before host date conversion and formatted
through effective Temporal slots.

Specifically:

1. convert operands to DateTime-formattable Temporal targets before same-kind
   validation so observable error order stays spec-shaped;
2. use `GetEffectiveTemporalSlots` for the Temporal target kind before
   producing strings or parts;
3. keep `formatRange` and `formatRangeToParts` on the same Temporal slot
   semantics instead of letting helper-specific code fall back to epoch
   milliseconds;
4. preserve range-part source tagging (`shared`, `startRange`, `endRange`) and
   collapsed-range behavior after slot filtering, not before it; and
5. keep timer/event-loop heavy async runtime tests in a non-parallel test
   collection when the shared event-loop scheduling model is under test.

## Consequences

- Future DateTimeFormat Temporal fixes should inspect all related surfaces:
  `format`, `formatToParts`, `formatRange`, and `formatRangeToParts`.
- Source tagging in `formatRangeToParts` is part of the observable contract.
  Reusing `formatRange` output is not enough unless the part boundaries and
  source labels are proven separately.
- Unsupported Temporal kinds and mixed Temporal kinds must fail at the
  conversion/validation boundary, not through incidental host conversion.
- Async runtime tests that exercise shared timers or event-loop progression
  should not depend on assembly-level parallel scheduling for stability.
- This ADR is caused by issue #768 / PR #938 and complements the root
  `.claude/rules/ecmascript-abstract-operations.md` rule.
