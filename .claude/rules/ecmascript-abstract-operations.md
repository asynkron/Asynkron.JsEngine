# ECMAScript Abstract Operation Order

When implementing ECMAScript built-ins or expression operators, model the named
abstract operation sequence directly before adding local type guards or
host-runtime shortcuts.

## Rules

1. Preserve observable coercion order. If the spec first performs an operation
   such as `ToDateTimeFormattable`, `ToNumber`, `ToPropertyKey`, or `Get`, do
   that before validating later same-kind, same-type, or option compatibility
   constraints.
2. Store the result of the abstract operation when later checks depend on its
   normalized shape. Re-detecting from the original `JsValue` can move errors or
   side effects ahead of required coercions.
3. For binary `+`, keep object `ToPrimitive` with the default hint before the
   string-concatenation or numeric-addition branch decision. If that primitive
   result is a `Symbol`, `ToString(Symbol)` and `ToNumber(Symbol)` must become
   catchable JavaScript `TypeError` completions, not host exceptions or
   shortcut-specific behavior.
4. For Intl Temporal formatting, route supported Temporal values through their
   effective Temporal slots instead of falling back to epoch milliseconds or
   `valueOf` behavior. The slots define which date/time fields are meaningful
   for `PlainDate`, `PlainDateTime`, `PlainTime`, `PlainYearMonth`, and
   `PlainMonthDay`.
5. Keep unsupported Temporal kinds explicit. `Temporal.ZonedDateTime` and
   distinct Temporal operand kinds should fail at the spec point that follows
   conversion to formattable operands, not through incidental host conversion
   failures.
6. For Intl `formatRangeToParts` helpers, preserve the source formatter's part
   boundaries and observable part objects. Do not flatten a formatted endpoint
   into one synthetic `integer` or `literal` part when formatter output already
   carries `currency`, `integer`, `minusSign`, grouping, decimal, or other
   semantic parts. Range composition should add only the range-level `source`
   label (`shared`, `startRange`, or `endRange`) and range separator or
   approximate-sign parts.
   Keep range string and range-parts composition on the same endpoint formatter,
   locale separator, and affix-sharing rules unless a spec proof requires an
   observable split.
7. For `Intl.DateTimeFormat.prototype.formatRangeToParts`, preserve source
   tagging after Temporal slot filtering. Do not prove only the formatted range
   string when the observable parts array has separate boundaries and labels.
8. Add focused coverage for both the error-order case and the successful
   normalized path. Include the exact Test262 method group or file cluster when
   the issue came from Test262.
9. For `Intl.DateTimeFormat` epoch-based formatting, do not let host
   `DateTimeOffset` range limits define ECMAScript proleptic Gregorian calendar
   fields. When the epoch is outside the host-supported range, derive Gregorian
   component fields from ECMAScript date math and keep `format`, `formatToParts`,
   `formatRange`, and `formatRangeToParts` on the same representation unless a
   helper proves it cannot observe out-of-range dates.
10. For `Intl.Locale`, keep BCP47 subtag parsing grammar-owned. Preserve empty
    subtags while validating user-provided option strings so leading, trailing,
    and doubled separators remain RangeError cases instead of disappearing
    during splitting.
11. For Locale variant handling, detect duplicates on the normalized variant
    subtag before sorting canonical output. Do not canonicalize a malformed
    variant sequence by routing it through a synthetic language tag first.
12. For Locale base-name and getter parsing, classify script, region, variant,
    and extension singleton boundaries from BCP47 subtag grammar. Digit-leading
    four-character subtags are variants, not scripts, and likely-subtags
    operations must preserve arbitrary extension singletons, not only `u`, `t`,
    or `x`.
13. For Unicode extension keyword parsing, keep the first duplicate keyword
    value and ignore later duplicates unless the spec text being implemented
    explicitly says otherwise.
14. For Object built-ins that begin with `ToObject`, keep primitive boxing and
    nullish rejection as separate proof cases. Primitive numbers, strings,
    booleans, symbols, and bigints are valid receivers after coercion, while
    `null` and `undefined` still throw `TypeError`; internal primitive-wrapper
    slots must not become ordinary enumerable or own descriptor properties.
15. For `Reflect.construct`, keep `target` and `newTarget` roles separate.
    `target` selects constructor behavior and allocation kind, including Array
    exotic allocation. `newTarget` selects the prototype path and realm
    fallback when `newTarget.prototype` is not an object. Do not let an Array
    `newTarget` turn an ordinary non-Array `target` into an Array, and do not
    miss cross-realm or proxied Array `target` cases just because they are not
    the current realm's Array constructor.

## Why

Issue #767 / PR #941 fixed `Intl.DateTimeFormat.prototype.formatRange` after
Temporal operands were handled too much like epoch-millisecond values. The
initial focused Test262 run exposed two durable traps: operands must be
converted to DateTime-formattable values before Temporal kind validation, and
supported Temporal `Plain*` objects must format through effective Temporal
slots rather than falling through to Date/valueOf behavior. The fix added local
Temporal range coverage and passed the focused `DateTimeFormat_prototype_formatRange`
Test262 method group.

Issue #768 / PR #938 fixed the same Temporal effective-slot lesson for
`formatRangeToParts`. That helper has an additional observable contract:
collapsed and non-collapsed ranges must assign the correct `source` labels to
each part after Temporal option filtering. The repair passed the focused
`DateTimeFormat_prototype_formatRangeToParts` Test262 method group.

Issue #809 / PR #1000 fixed `Intl.NumberFormat.prototype.formatRangeToParts`
after the implementation flattened formatted endpoints into hard-coded integer
parts. Test262 expected the range result to reuse the existing
`IntlNumberFormatResult.Parts` boundaries for currency and integer output,
preserve collapsed or rounding-equal ranges as `approximatelySign` plus shared
formatter parts, and create `type`, `value`, and `source` as ordinary data
properties. Future Intl range-parts work should prove the parts array shape,
not only the final range string.

Issue #808 / PR #1004 fixed follow-up range composition drift after
`formatRangeToParts` and `formatRange` no longer shared all endpoint formatting
and affix logic. Mixed-sign currency ranges must not share a suffix that makes
one endpoint lose its sign, the `pt-PT` hyphen separator override must not leak
to `pt-BR`, and joined parts should match `formatRange` when both helpers
observe the same range string. Future Intl range work should pin those cases
locally and rerun both focused NumberFormat range Test262 method groups.

Issue #766 / PR #942 fixed `Intl.DateTimeFormat.prototype.format` after
out-of-range proleptic Gregorian dates were converted through
`DateTimeOffset`, whose supported range is narrower than ECMAScript TimeClip.
That clamped BC dates to the host boundary year and also risked drifting across
parts/range helpers. Future Intl date work needs an explicit proleptic-safe
component path, with the exact `DateTimeFormat_prototype_format` Test262 group
and local parts/range regressions proving the behavior.

Issue #795 / PR #988 fixed the `Intl402Tests.Locale` Test262 cluster after
Locale variant option parsing, base-name extraction, Unicode extension keyword
deduplication, and digit-leading variant classification drifted from BCP47
grammar. The durable lesson is that Locale tags are structured language tags,
not generic dash-joined strings: preserve separator errors during validation,
dedupe normalized variants before sorting, stop base-name extraction at any
extension singleton, and keep the first duplicate Unicode keyword value. Future
Locale work should pair local grammar regressions with the focused
`Name=Locale` Test262 method group.

Issue #813 / PR #1008 added local regressions for
`Object.getOwnPropertyDescriptors` after the focused Test262 method group was
already green. The behavior to keep pinned is the abstract-operation split:
`ToObject(42)` yields a primitive wrapper with no public own descriptors, but
`ToObject(null)` and `ToObject(undefined)` still throw `TypeError`. Future
Object built-in work should prove both the primitive-success path and the
nullish-error path locally so wrapper implementation details such as
`__value__` do not leak into descriptor enumeration.

Issue #817 / PR #1018 fixed `Reflect.construct` after the Array allocation
special case mixed the roles of `target` and `newTarget`. A cross-realm or
proxied Array `target` must still allocate a `JsArray`, while an Array
`newTarget` with an ordinary target only contributes prototype/realm fallback
and must not change the object's allocation kind. Future construction work
should prove the focused
`Name=ReflectConstruct_ProxiedNewTargetUsesTargetRealm` Test262 method group
and a local non-Array-target regression before widening.

Related ADR: `docs/adrs/0032-keep-reflect-construct-target-allocation-newtarget-prototype-split.md`.

Issue #1027 / PR #1100 added focused regressions after the Test262
`Expressions_addition` crash cluster around Symbol wrapper coercion. The
durable lesson is that binary addition is also abstract-operation work: wrapper
operands must run `ToPrimitive` with the default hint before branch selection,
and the resulting Symbol primitive must surface through catchable JavaScript
`TypeError` conversion paths for both string concatenation and numeric
addition. Future operator-coercion work should pin both the error path and the
`Symbol.toPrimitive` hint before relying on the focused Test262 method group.
