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
15. For Intl constructors that accept options, use the ECMA-402 ToObject
    options path when the constructor requires it. `undefined` remains absent,
    `null` throws at options coercion, and non-null primitives are boxed before
    option property reads. Keep the constructor's spec option read order after
    coercion.
16. For `Reflect.construct`, keep `target` and `newTarget` roles separate.
    `target` selects constructor behavior and allocation kind, including Array
    exotic allocation. `newTarget` selects the prototype path and realm
    fallback when `newTarget.prototype` is not an object. Do not let an Array
    `newTarget` turn an ordinary non-Array `target` into an Array, and do not
    miss cross-realm or proxied Array `target` cases just because they are not
    the current realm's Array constructor. Concrete typed-array constructors
    are also `target`-selected constructor behavior: route them through the
    typed-array constructor path before generic `newTarget.prototype`
    resolution, and prove invalid primitive typed-array arguments throw before
    observable prototype access. WHY: issue #871 / PR #1258 fixed
    `Reflect.construct` typed-array construction after generic prototype
    resolution moved ahead of the typed-array `ToIndex` error boundary.
16a. For constructor paths that receive a `NewTarget` `JsValue`, do not use
    `AsObject<IJsCallable>()` behind an `IsObject` guard. A non-callable object
    can be a valid object value but not a constructor/callable implementation;
    convert that mismatch into a JavaScript `TypeError` with
    `TryGetObject<IJsCallable>` before passing the value to
    `ConstructWithNewTarget`. WHY: issue #1046 / PR #1282 fixed
    `Intl.ListFormat` after a non-callable object `NewTarget` crashed through a
    host cast instead of producing a catchable constructor error.
16b. For Promise capability construction, keep resolve and reject capture slots
    independent. A custom species constructor can call the executor more than
    once, so duplicate-call checks must fail after either slot was already set,
    and post-construction callable validation must report resolve and reject
    separately. WHY: issue #1056 / PR #1288 fixed
    `Promise.prototype.then` after its local capability helper used pair-level
    guards that drifted from the constructor helper and failed the focused
    Test262 `Promise_prototype_then` fixture.
17. For Intl built-ins that coerce call arguments through `ToNumber`, use the
    active evaluation context and propagate abrupt completions before later
    numeric validation such as finite-number checks. Raw `JsValue.AsNumber()`,
    fresh realm contexts, or other non-active-context shortcuts can skip or
    isolate observable `valueOf`/`toString` errors and turn the wrong condition
    into the visible failure.
18. For Temporal `relativeTo` property-bag conversion, resolve the calendar at
    the `calendar` step before deciding calendar-specific property reads. Read
    and coerce `era` and `eraYear` at their alphabetical slots only when the
    resolved calendar is era-capable; do not read missing ISO-only `era` fields
    just to share a generic field list. For fixed-offset ZonedDateTime strings,
    validate explicit offset agreement without forcing valid boundary instants
    through host `DateTimeOffset`.
19. For `Temporal.Instant.prototype.toLocaleString`, keep absent or undefined
    options distinct from defined non-format option bags. Absent options should
    let `Intl.DateTimeFormat` compute its own defaults before formatting the
    Instant. Defined non-format options such as `timeZone` should still receive
    Instant date/time defaults, but Instant defaults must not inject
    `timeZoneName`; keep ZonedDateTime's time-zone-name default path separate.
20. For Temporal `PlainDate.from` and `PlainDateTime.from` non-ISO property
    bags, preserve the calendar-visible `year`, `month` or `monthCode`, and
    `day` fields after overflow handling. Use calendar-to-ISO conversion as a
    validation projection only; do not store the converted ISO fields back into
    the resulting object. Treat `era` and `eraYear` as calendar-dependent:
    calendars without era support ignore them when `year` is explicit and still
    require `year` when it is absent.
21. For Temporal `PlainDateTime.prototype.toLocaleString`, keep PlainDateTime
    as wall-clock component formatting even when a resolved `timeZone` option is
    supplied, but route hour output through the shared `Intl.DateTimeFormat`
    hour-cycle formatting helpers. `h23` and `h24` numeric hours still need the
    same zero-padding behavior as the epoch/proleptic DateTimeFormat paths; do
    not fix Temporal output with a Temporal-only string special case.
22. For Temporal duration rounding, do not hard-code midpoint tie behavior in
    special calendar or ZonedDateTime branches. Route midpoint decisions through
    the shared rounding abstract operation helper so every rounding mode,
    including `halfCeil`, `halfFloor`, `halfTrunc`, and odd-quotient
    `halfEven`, keeps the same semantics as the generic path.
23. For `Temporal.PlainDate.prototype.toZonedDateTime`, keep omitted or
    explicitly `undefined` `plainTime` distinct from explicit `PlainTime`.
    Omitted/undefined `plainTime` must use true start-of-day semantics through
    `GetStartOfDayInstant`; explicit `new Temporal.PlainTime()` remains on the
    PlainDateTime/midnight disambiguation path. Do not normalize absence into a
    zero-valued Temporal object before the spec branch that observes absence.
24. For `Temporal.PlainDateTime.prototype.with` on non-ISO calendars, merge
    partial date overrides against the receiver's observable calendar fields,
    not its internal ISO storage fields. Preserve the receiver's default
    `monthCode` across year changes when no explicit month or monthCode is
    supplied, resolve supplied `monthCode` in the receiver calendar and target
    calendar year, and convert to internal ISO storage only after
    calendar-field merge and overflow handling.
25. For `Temporal.PlainDate.prototype.with` on non-ISO calendars, merge
    partial date overrides against the receiver's observable calendar fields,
    not its internal ISO storage fields. When `month` and `monthCode` are both
    omitted, preserve a valid visible default `monthCode` in the target year,
    but let invalid leap-month defaults fall through the numeric month overflow
    path under `overflow: "constrain"` so option ordering and constrain
    semantics remain observable.
26. For `Temporal.PlainDate.prototype.until` and adjacent PlainDate difference
    work, matching BCL-backed non-ISO calendars must compute month and year
    largest-unit differences in calendar space, not through ISO month/year
    arithmetic over the stored ISO projection. Convert endpoints to
    calendar-visible fields before balancing months or years, keep day/week
    largest-unit behavior on elapsed ISO dates unless the spec path says
    otherwise, and resolve PlainDate property-bag `monthCode` values through the
    resolved calendar/year for leap-month-aware calendars.
27. For `Temporal.PlainMonthDay.from` property bags, keep ISO field reads in
    observable spec order and keep era reads calendar-dependent. ISO bags must
    read `calendar`, `day`, `month`, `monthCode`, `year`, then options; they
    must not observe `era` or `eraYear` getters. Non-ISO paths may read
    `era`/`eraYear` for validation, but should read them once and reuse the
    observed presence instead of re-reading later.
28. For `Temporal.PlainMonthDay.prototype.toLocaleString` and adjacent
    `Intl.DateTimeFormat` Temporal formatting, keep calendar-visible fields
    separate from reference ISO slots. Use `ReferenceYear`, `ReferenceMonth`,
    and `ReferenceDay` when constructing host date or epoch values for
    PlainMonthDay; use the receiver calendar to format that reference date, not
    to supply ISO constructor fields. PlainMonthDay `dateStyle` expansion must
    also filter to month/day fields for that Temporal kind instead of leaking a
    year field through shared date-style defaults.
29. For `Temporal.PlainMonthDay.prototype.toString`, treat `calendarName` as
    calendar-annotation policy, not as a request to collapse all calendars to
    the ISO short month-day shape. `calendarName: "never"` must keep ISO
    receivers as `MM-DD`, but non-ISO receivers must return the non-annotated
    reference ISO date `YYYY-MM-DD`. Keep `auto`, `always`, and `critical`
    behavior on their existing annotation branches unless the spec path proves
    a separate change.
30. For `Temporal.PlainTime.from` property bags, keep leap-second and
    out-of-range time fields on the shared Temporal overflow path. Default and
    `overflow: "constrain"` calls must normalize `second: 60` and higher
    seconds to the valid maximum second, while `overflow: "reject"` must throw
    `RangeError` at the time-range validation point.
31. For `Temporal.PlainYearMonth.from` and adjacent PlainYearMonth getters or
    string formatting, keep calendar-visible year/month fields separate from
    the stored ISO reference date. Map the reference date back through the
    receiver calendar for observable `year`, `month`, and `monthCode`, use the
    stored ISO reference date directly for non-ISO string forms that need a
    date, and treat host BCL calendar range/leap-month limits as helper
    boundaries rather than complete ECMA-402 semantics.
32. For `Temporal.PlainYearMonth.prototype.toLocaleString` and adjacent
    `Intl.DateTimeFormat` Temporal formatting, keep date-style expansion in the
    field domain of PlainYearMonth. `dateStyle` may format year/month but must
    not leak the reference day, and non-ISO month names must come from the
    receiver calendar's year/month domain rather than a Gregorian
    `DateTimeOffset` month.
33. For `Temporal.PlainTime.prototype.toLocaleString` and adjacent
    `Intl.DateTimeFormat` Temporal formatting, keep PlainTime output
    timezone-neutral. A supplied `timeZone` remains observable for
    `Intl.DateTimeFormat` option resolution, but PlainTime has no date or
    instant and must not be shifted through that zone. Keep output on the
    receiver's time fields and shared hour-cycle helpers, not a synthetic
    instant-backed host conversion.
34. For `Temporal.ZonedDateTime` and other instant-backed Temporal
    constructors, validate the normalized epoch nanoseconds against the shared
    Temporal instant bounds after the spec-required BigInt coercion and before
    constructing or wrapping a `JsTemporalInstant`. Exact min/max bounds remain
    valid; min-1/max+1 must throw `RangeError`. Do not rely on BigInteger
    storage, host date conversion, or downstream string formatting to enforce
    the representable instant range.
35. For `Temporal.ZonedDateTime.compare` and adjacent ZonedDateTime
    property-bag conversion, preserve observable absent-field order separately
    from present-field validation. Proxy or observer bags without own
    `era`/`eraYear` properties must not observe synthetic missing-field reads
    merely because ordinary bags with present era fields still need coercion
    and validation. Prove this with the focused
    `Name=Temporal_ZonedDateTime_compare` Test262 method group.
36. For `Temporal.ZonedDateTime.prototype.since` and `.until` (and the shared
    `DifferenceTemporalZonedDateTime` abstract operation), keep the spec step
    order between the time-only fast path and the time-zone equality check
    explicit. The hour-or-smaller `LargestUnit` branch returns from epoch
    nanosecond difference before `TimeZoneEquals`, so cross-zone hour-largest
    differences are valid; only calendar-unit (day or larger) differences
    require matching time zones. Compare time zones through a canonical
    identifier helper such as `CanonicalizeTimeZoneIdForComparison` so
    fixed-offset spellings (`+01`, `+0100`, `+01:00`), named IANA zones, and
    mixed pairs are checked uniformly. Do not bypass the equality check for
    fixed-offset operands, since `+01:00` vs `+02:00` is a real mismatch that
    must still throw `RangeError` for calendar-unit differences.
37. For `Temporal.ZonedDateTime` offset lookups (`offsetNanoseconds` getter,
    `offset` getter, local `PlainDateTime` projection, and `AddZonedDateTime`
    time-only fast path), read the offset from epoch nanoseconds directly; do
    not round-trip through `Instant.ToDateTimeOffset()` or a formatted offset
    string. Convert `BigInteger` epoch nanoseconds to .NET ticks with floor
    division (`epochNs / 100`, rounding toward −∞ for negative remainders)
    before calling `TimeZoneInfo.GetUtcOffset(...)`, so instants in the
    interval `[transition − 1 ns, transition)` stay on the pre-transition side
    of the DST boundary. Return `OffsetNanoseconds` directly from the stored
    model value rather than parsing the formatted offset string, which drops
    sub-second precision. For time-only durations with zero years, months,
    weeks, and days, apply the nanosecond delta directly to the stored instant
    and validate against the shared Temporal instant bounds instead of routing
    through the local PlainDateTime wall-clock path, which fails for skipped or
    ambiguous wall-clock times at DST transitions.
38. When a regression test claims to prove an abstract-operation coercion path,
    make the coercion observable. Use object wrappers with `valueOf`,
    `toString`, or `Symbol.toPrimitive` call tracking, and assert the call
    count/order/hint where relevant. Raw primitives prove only the already
    normalized storage path; they do not prove that `ToNumber`, `ToBigInt`,
    `ToPrimitive`, or similar coercion hooks actually ran.
39. For optional built-in arguments that flow into a named abstract operation,
    distinguish argument absence from explicit `undefined` only when the spec
    branch itself observes that distinction. Do not pre-stringify explicit
    `undefined` or route it through a legacy compatibility shortcut before an
    operation such as `RegExpCreate` gets the original value.
40. For `Temporal.PlainYearMonth.prototype.with` on non-ISO calendars, merge
    partial overrides against the receiver's observable calendar `year`,
    `month`, and `monthCode`, not the stored ISO reference projection. Read
    `overflow` before resolving omitted month/monthCode defaults, preserve the
    receiver `monthCode` only when neither `month` nor `monthCode` is supplied,
    and validate explicit leap `monthCode` overrides in the target calendar
    year. Why: issue #854 / PR #1186 showed Hebrew leap month `M05L` can be a
    valid receiver default, a constrain fallback, or an explicit reject case
    depending on whether the field was inherited or supplied.
41. For `Temporal.ZonedDateTime.prototype.since` and `.until` date-unit
    rounding bound validation, validate the rounded candidate as a full
    date-time built from the receiver wall-clock time plus the normalized time
    remainder. Do not validate the rounded date at synthetic midnight: issue
    #864 / PR #1245 showed that the lower Temporal boundary can reject midnight
    while accepting the receiver's actual `00:00:00.000000001` time, and the
    normalized remainder is still needed to preserve the original
    out-of-range Test262 throw.
42. For `instanceof` custom `@@hasInstance`, preserve the full spec hook
    shape: get and call the method with the right-hand side as `this`, pass the
    left-hand side as the sole argument, propagate abrupt completion or stopped
    evaluation before producing a value, and return `ToBoolean(result)` rather
    than a local truthiness shortcut. Why: issue #1036 / PR #1275 fixed
    `Symbol.hasInstance` Test262 failures where the custom hook result and
    call shape had to match ECMAScript `InstanceofOperator` semantics.

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

Issue #822 / PR #1110 fixed `Intl.RelativeTimeFormat` constructor
`options-toobject` Test262 failures after the constructor used the strict
object-only options path. ECMA-402 constructors can observe option coercion:
`undefined` means no options, `null` throws before property reads, and non-null
primitive options are boxed before properties such as `localeMatcher`,
`numberingSystem`, `style`, and `numeric` are read in spec order. Future Intl
constructor work should reuse the shared ToObject-compatible options helper and
prove the focused `Name=RelativeTimeFormat_constructor_constructor` or owning
constructor Test262 method group.

Related ADR: `docs/adrs/0039-keep-intl-constructor-options-toobject-coercion.md`.

Issue #1046 / PR #1282 fixed `Intl.ListFormat` constructor
`NewTarget` handling after the constructor guarded only `newTarget.IsObject`
before casting to `IJsCallable`. Test262 exposed a non-callable object
`NewTarget` path where the implementation threw a host cast exception instead
of a JavaScript `TypeError`. Future constructor work should guard
`NewTarget` with `TryGetObject<IJsCallable>` and prove the owning constructor
Test262 method group before widening.

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

Issue #870 / PR #1226 fixed the Test262
`TypedArrayConstructors_ctors_objectArg` closeout after review caught that the
initial BigInt typed-array regressions passed raw BigInt primitives directly.
Those assertions only proved element storage, not observable per-element
`ToBigInt` coercion. The repair used object elements with
`Symbol.toPrimitive` call tracking and `callCount` assertions for
`BigInt64Array` and `BigUint64Array`, matching the sibling `valueOf`-based
`ToNumber` tests. Future typed-array or collection-constructor coercion tests
should make the named abstract operation observable before claiming the
coercion path is pinned.

Issue #823 / PR #1113 fixed `Intl.RelativeTimeFormat.prototype.format` after
the value argument used a non-observable numeric shortcut before finite-number
validation. Issue #824 / PR #1117 repeated the same abstract-operation lesson
for `formatToParts`: the shared `format`/`formatToParts` argument extraction
still used a fresh realm context, which isolated object-coercion abrupt
completions from the active call. Future Intl argument-coercion work should run
`ToNumber` through the active `EvaluationContext`, rethrow abrupt completion
with `ThrowSignal` before checking `double.IsFinite`, and prove both observable
object coercion and the ordinary finite-number path with the focused
`Name=RelativeTimeFormat_prototype_format` and
`Name=RelativeTimeFormat_prototype_formatToParts` Test262 method groups.

Issue #1036 / PR #1275 fixed `instanceof` custom `Symbol.hasInstance`
handling after the focused Test262 symbol-hasinstance cluster exposed two
operator-hook hazards: the hook call shape is observable (`this` must be the
right-hand object and the candidate must be the only argument), and the return
path is the spec `ToBoolean` result after abrupt-completion propagation, not an
engine-local shortcut. Future `instanceof` or well-known-symbol operator-hook
work should add focused local regressions for call shape and result coercion,
then run the owning focused Test262 group before widening.

Issue #832 / PR #1128 fixed `Temporal.Duration.compare` after `relativeTo`
conversion first skipped `era`/`eraYear` reads entirely, then needed a
review-bounce repair to restore those reads only for era-capable calendars.
The durable rule is that Temporal property-bag conversion is observable and
calendar-dependent: ISO bags must not observe missing era fields, while
era-capable calendars must still coerce era fields before later fields such as
`hour`. The same issue fixed fixed-offset `relativeTo` strings at Temporal's
representable range boundary by validating offset agreement without routing
valid boundary instants through host `DateTimeOffset`. Future Temporal
`relativeTo` work should pin both observable property order and boundary string
handling with the focused `Name=Temporal_Duration_compare` Test262 method
group.

Related ADR: `docs/adrs/0046-keep-temporal-relative-to-conversion-observable.md`.

Issue #836 / PR #1134 fixed
`Temporal.Instant.prototype.toLocaleString` after the implementation injected
the same Temporal date/time/time-zone-name defaults for absent options, defined
non-format options, and lone component options. The durable lesson is that
Temporal locale formatting defaults are observable Intl option semantics:
absent/undefined options must match `Intl.DateTimeFormat(...).format(instant)`,
defined non-format options still need Instant date/time default fields, a lone
`timeZoneName` keeps date/time defaults plus the explicit zone-name request,
and ZonedDateTime owns its separate time-zone-name default. Future work should
prove the focused `Name=Temporal_Instant_prototype_toLocaleString` Test262
method group before widening.

Related ADR:
`docs/adrs/0047-keep-temporal-instant-locale-defaults-absent-options-split.md`.

Issue #837 / PR #1137 fixed `Temporal.PlainDateTime.from` after non-ISO
calendar property bags reused the converted ISO date as the object's visible
date fields. The durable lesson is that Temporal property-bag conversion can
need two representations at once: source calendar fields remain observable on
the Temporal object, while the converted ISO projection is only for range
validation. Future `PlainDateTime.from` work should preserve calendar-visible
fields, keep era handling calendar-dependent, and prove the focused
`Name=Temporal_PlainDateTime_from` Test262 method group.

Related ADR: `docs/adrs/0048-keep-temporal-plaindatetime-calendar-fields-observable.md`.

Issue #840 / PR #1160 fixed the same Temporal property-bag boundary for
`Temporal.PlainDate.from`: Hebrew and other non-era calendars must not observe
throwing `era` or `eraYear` getters when `year` is explicit, and the resulting
`PlainDate` must keep the source calendar's visible fields after BCL-backed
calendar validation. Future `PlainDate.from` work should preserve the
calendar-visible fields, make era reads depend on `CalendarUsesEras`, and prove
the focused `Name=Temporal_PlainDate_from` Test262 method group.

Related ADR: `docs/adrs/0057-keep-temporal-plaindate-calendar-fields-observable.md`.

Issue #838 / PR #1146 fixed `Temporal.PlainDateTime.prototype.toLocaleString`
after the `resolved-time-zone.js` Test262 fixture expected the supplied
`Pacific/Apia` time zone to remain resolved but not shift PlainDateTime's
wall-clock fields. The implementation already preserved wall-clock fields, but
the Temporal component path bypassed shared hour-cycle padding and formatted
`h23` numeric midnight as `0` instead of `00`. Future Temporal locale-format
work should keep PlainDateTime on the component path, keep `timeZone` resolved,
and prove hour-cycle output with the focused
`Name=Temporal_PlainDateTime_prototype_toLocaleString` Test262 method group.

Related ADR:
`docs/adrs/0049-keep-temporal-plaindatetime-locale-hours-on-shared-hourcycle-formatting.md`.

Issue #841 / PR #1158 fixed
`Temporal.PlainDate.prototype.toZonedDateTime` after the skipped-midnight
Test262 fixture for `America/Toronto` exposed that omitted or explicitly
undefined `plainTime` is not equivalent to explicit midnight. Future PlainDate
zoning work should route absent `plainTime` through true start-of-day semantics,
keep explicit `PlainTime` on the PlainDateTime/midnight disambiguation path,
and prove the focused `Name=Temporal_PlainDate_prototype_toZonedDateTime`
Test262 method group before widening.

Related ADR:
`docs/adrs/0056-keep-temporal-plaindate-zoning-start-of-day-distinct.md`.

Issue #834 / PR #1135 fixed `Temporal.Duration.prototype.round` after the
ZonedDateTime month midpoint fast path initially collapsed rounding modes into
a small hand-written list. Review caught that `halfCeil` and odd-quotient
`halfEven` did not match the shared `RoundToIncrement` tie semantics. Future
Temporal rounding work should use the shared rounding helper for midpoint
branches and prove the focused `Name=Temporal_Duration_prototype_round`
Test262 method group before widening.

Related ADR:
`docs/adrs/0054-keep-temporal-duration-rounding-ties-shared.md`.

Issue #839 / PR #1159 fixed `Temporal.PlainDateTime.prototype.with` after
non-ISO receiver defaults were merged through internal ISO date fields. The
durable lesson extends issue #837's property-bag rule to receiver-based updates:
the receiver's calendar-visible `year`, `month`, `day`, and `monthCode` are the
defaults and comparison basis for `with`, while the ISO projection is only the
storage/range representation after calendar-date merge. Future
`PlainDateTime.prototype.with` work should prove the focused
`Name=Temporal_PlainDateTime_prototype_with` Test262 method group, starting
with the `non-iso-calendar-fields.js` fixture.

Related ADR:
`docs/adrs/0055-keep-temporal-plaindatetime-with-calendar-date-fields.md`.

Issue #843 / PR #1169 fixed `Temporal.PlainDate.prototype.with` after Gregorian
era fields and Hebrew calendar fields were merged against the wrong
representation. The durable lesson mirrors PlainDateTime.with but adds a
PlainDate-specific leap-month ordering trap: receiver defaults come from
calendar-visible fields, the internal ISO projection is only the final storage
representation, and an omitted Hebrew leap `monthCode` must not throw before
`overflow: "constrain"` can fall back through the numeric month path. Future
`PlainDate.prototype.with` work should prove the focused
`Name=Temporal_PlainDate_prototype_with` Test262 method group and keep local
coverage for Hebrew leap-month constrain behavior.

Related ADR:
`docs/adrs/0067-keep-temporal-plaindate-with-calendar-date-fields.md`.

Issue #854 / PR #1186 fixed `Temporal.PlainYearMonth.prototype.with` after
non-ISO receiver defaults, era-capable calendar fields, and Hebrew leap-month
handling could be merged or validated in the wrong domain. The durable lesson
extends PlainDate/PlainDateTime `.with`: merge against the receiver's
calendar-visible year/month fields, read `overflow` before resolving omitted
leap-month defaults, and distinguish inherited defaults from explicit
`monthCode` overrides. Future `PlainYearMonth.prototype.with` work should prove
the focused `Name=Temporal_PlainYearMonth_prototype_with` Test262 method group
and keep local Hebrew leap-month constrain/reject coverage.

Related ADR:
`docs/adrs/0074-keep-temporal-plainyearmonth-with-calendar-year-month-fields.md`.

Issue #842 / PR #1163 fixed `Temporal.PlainDate.prototype.until` after matching
Chinese lunisolar PlainDate endpoints were accepted as non-ISO calendars but
balanced month/year largest units through ISO date arithmetic. The durable
lesson is that PlainDate difference has two unit domains: day/week counts can
stay elapsed ISO-date differences, while month/year largest units must use the
resolved calendar's visible year/month/day fields for BCL-backed non-ISO
calendars. Future PlainDate difference work should prove the focused
`Name=Temporal_PlainDate_prototype_until` Test262 method group, starting with
the lunisolar leap-month fixture.

Related ADR:
`docs/adrs/0058-keep-temporal-plaindate-difference-calendar-unit-arithmetic-owned.md`.

Issue #844 / PR #1162 fixed `Temporal.PlainMonthDay.from` after ISO property
bags observed `era` and `eraYear` reads before `month`, `monthCode`, `year`,
and options. The durable lesson is that `PlainMonthDay` follows the same
observable Temporal property-bag discipline as the adjacent PlainDate and
PlainDateTime readers, but ISO `PlainMonthDay` specifically must not touch era
fields. Future `PlainMonthDay.from` work should prove the focused
`Name=Temporal_PlainMonthDay_from` Test262 method group and review clone/string
paths separately from property bags.

Related ADR:
`docs/adrs/0059-keep-temporal-plainmonthday-field-order-calendar-dependent.md`.

Issue #845 / PR #1167 fixed `Temporal.PlainMonthDay.prototype.equals` after
constructor and string conversion paths normalized calendar aliases but still
stored non-ISO month-day reference slots through ad hoc converted fields. The
durable lesson is that PlainMonthDay equality depends on both canonical calendar
identity and normalized reference ISO slots. Future PlainMonthDay equality or
conversion work should route ISO reference-date construction through the shared
non-ISO month-day helper, add explicit ISO-date conversion coverage before
accepting a calendar ID, and prove the focused
`Name=Temporal_PlainMonthDay_prototype_equals` Test262 method group plus local
coverage for accepted non-ISO constructor calendars.

Related ADR:
`docs/adrs/0065-keep-temporal-plainmonthday-reference-normalization-shared.md`.

Issue #846 / PR #1171 fixed
`Temporal.PlainMonthDay.prototype.toLocaleString` after Islamic calendar
date-style formatting mixed calendar-visible month/day fields with the
reference year to build host `DateTimeOffset` values. PlainMonthDay has
calendar-visible fields and reference ISO slots; those are different domains.
Future PlainMonthDay locale-format work should build host date/epoch values
from the reference slots, apply the receiver calendar during formatting, and
prove the focused `Name=Temporal_PlainMonthDay_prototype_toLocaleString`
Test262 method group. The same delivery's quality repair reaffirmed that
non-ISO PlainDate storage remains ISO-backed while calendar fields are exposed
through calendar-part helpers.

Related ADR:
`docs/adrs/0060-keep-temporal-plainmonthday-locale-formatting-on-reference-slots.md`.

Issue #847 / PR #1172 fixed `Temporal.PlainMonthDay.prototype.toString` after
`calendarName: "never"` routed every receiver to the ISO short `MM-DD` shape.
The durable lesson is that `calendarName` removes or adds the calendar
annotation; it does not choose the field domain. ISO PlainMonthDay receivers
remain `MM-DD`, while non-ISO receivers must preserve their reference ISO date
without annotation, for example `1972-05-02`. Future PlainMonthDay string work
should prove the focused `Name=Temporal_PlainMonthDay_prototype_toString`
Test262 method group and local ISO/non-ISO `calendarName: "never"` coverage.
The same delivery's quality repair reaffirmed that non-ISO PlainDate storage is
ISO-backed while calendar-visible fields are exposed through calendar helpers.

Related ADR:
`docs/adrs/0061-keep-temporal-plainmonthday-tostring-reference-date.md`.

Issue #852 / PR #1180 fixed
`Temporal.PlainYearMonth.prototype.toLocaleString` after date-style formatting
reused the generic `DateTimeOffset` component path and flattened calendar
year/month output through Gregorian host month names. The durable lesson is
that PlainYearMonth style formatting has its own field domain: output year and
month only, keep the reference day internal, and derive non-ISO month names
from the receiver calendar. Future PlainYearMonth locale-format work should
prove the focused `Name=Temporal_PlainYearMonth_prototype_toLocaleString`
Test262 method group plus local Gregorian/non-ISO month-name coverage.

Related ADR:
`docs/adrs/0063-keep-temporal-plainyearmonth-locale-formatting-on-calendar-fields.md`.

Issue #848 / PR #1173 added local regression coverage for
`Temporal.PlainTime.from` after the Test262 leap-second property-bag fixture
needed a local pin. Future PlainTime property-bag and shared time-overflow work
should keep default/constrain normalization distinct from reject validation and
prove both the focused `Name=Temporal_PlainTime_from` Test262 method group and
local coverage for `second: 60`, `second: 61`, and `overflow: "reject"`.

Issue #850 / PR #1181 fixed `Temporal.PlainYearMonth.from` after non-ISO
calendar property bags, era remapping, and Chinese/Hebrew reference-day cases
mixed calendar-visible fields with the stored ISO reference date or trusted BCL
calendar support too broadly. Future PlainYearMonth work should map the stored
reference date back through the receiver calendar for getters, use the stored
ISO reference date directly for non-ISO string forms, resolve month codes in
the resolved calendar/year, and prove the focused
`Name=Temporal_PlainYearMonth_from` Test262 method group.

Related ADR:
`docs/adrs/0064-keep-temporal-plainyearmonth-reference-day-calendar-owned.md`.

Issue #851 / PR #1178 fixed `Temporal.PlainYearMonth.prototype.equals` after
full-date string conversion canonicalized the `islamicc` calendar alias and
then recomputed the PlainYearMonth reference day through the canonical calendar.
The durable lesson is that PlainYearMonth full-date strings already supply the
ISO reference day used for equality; calendar annotation validation and alias
canonicalization must not change that parsed reference-day domain. Property
bags remain separate because their calendar-visible fields may require
calendar-to-ISO conversion. Future PlainYearMonth equality or conversion work
should prove the focused `Name=Temporal_PlainYearMonth_prototype_equals`
Test262 method group, starting with the `canonicalize-calendar.js` fixture,
and include local coverage for both string and property-bag calendar aliases.

Related ADR:
`docs/adrs/0062-keep-temporal-plainyearmonth-string-reference-day.md`.

Issue #849 / PR #1174 fixed
`Temporal.PlainTime.prototype.toLocaleString` after the resolved time-zone
fixture could shift PlainTime output through `Pacific/Apia`. The durable lesson
is that PlainTime locale formatting must preserve resolved `timeZone` option
semantics without treating the receiver as an instant or date-bearing value.
Future PlainTime locale-format work should prove the focused
`Name=Temporal_PlainTime_prototype_toLocaleString` Test262 method group plus
local coverage using offset-sensitive times.

Related ADR:
`docs/adrs/0066-keep-temporal-plaintime-locale-formatting-timezone-neutral.md`.

Issue #855 / PR #1189 fixed `Temporal.ZonedDateTime` constructor limit
failures after the constructor accepted epoch nanoseconds outside Temporal's
representable instant range and wrapped them in a `JsTemporalInstant`. The
durable lesson is that instant-backed constructors need an explicit shared
Temporal bounds check at the constructor boundary after coercion, while keeping
the exact boundary values accepted. Future ZonedDateTime or instant-backed
constructor work should prove both local min/max acceptance and min-1/max+1
rejection, plus the focused `Name=Temporal_ZonedDateTime` Test262 method group.

Issue #856 / PR #1191 fixed `Temporal.ZonedDateTime.compare` after the
property-bag path unconditionally attempted absent `era`/`eraYear` reads. That
preserved validation for ordinary present fields but made proxy/observer bags
see extra missing-property probes before later fields. The durable lesson is
that Temporal property-bag readers must distinguish absent-field observability
from present-field validation: skip synthetic absent `era`/`eraYear` reads for
observable bags, but still coerce and validate ordinary own era fields when
present. This remains under ADR 0046's broader Temporal property-bag
observability rule and should be proven with the focused
`Name=Temporal_ZonedDateTime_compare` Test262 method group.


Issue #861 / PR #1198 fixed `Temporal.ZonedDateTime.prototype.since` and
`.until` after the `TimeZoneEquals` check ran ahead of the hour-largest
time fast path and the `FixedOffset` bypass silently accepted non-equivalent
fixed-offset operands for calendar-unit differences. The durable lesson is
that `DifferenceTemporalZonedDateTime` has two distinct spec ordering
requirements: hour-or-smaller largest units return via epoch nanosecond
difference before any time-zone equality check, and calendar-unit largest
units require a single canonical identifier comparison that handles
fixed-offset, named, and mixed pairs uniformly. Future ZonedDateTime
difference work should pin both sides locally (hour-largest cross-zone
success, day-largest mismatch throw across named/fixed/mixed, and equivalent
fixed-offset spelling success) before relying on the focused
`Name=Temporal_ZonedDateTime_prototype_since` and
`Name=Temporal_ZonedDateTime_prototype_until` Test262 method groups.

Related ADR:
`docs/adrs/0070-keep-temporal-zoneddatetime-difference-step-order-explicit.md`.

Issue #864 / PR #1245 fixed `Temporal.ZonedDateTime.prototype.until` after
date-unit rounding-increment bound validation used a rounded ISO date at
midnight. That preserved one out-of-range Test262 throw but over-rejected valid
negative-boundary values where the receiver wall-clock time was just after
midnight. The durable lesson is that `ValidateZonedDateTimeDateRoundingBound`
must validate the full receiver-relative date-time: combine the receiver's time
fields with the normalized time remainder, carry or borrow days from that
nanosecond total, then call the shared `RejectISODateTimeRange` helper. Future
ZonedDateTime since/until rounding work should prove both the focused
negative-boundary internal regression and the
`roundingincrement-addition-out-of-range` Test262 fixture before widening to the
full `Name=Temporal_ZonedDateTime_prototype_until` method group.

Related ADR:
`docs/adrs/0076-keep-temporal-zoneddatetime-rounding-bound-wallclock-preserving.md`.

Issue #860 / PR #1197 fixed `Temporal.ZonedDateTime` offset getters and time-only
`add`/`subtract` after DST transition-boundary instants returned the wrong UTC
offset. The implementation routed offset lookup through `Instant.ToDateTimeOffset()`
(which truncates to 100 ns tick precision and can slide across the transition
boundary) and re-parsed the formatted offset string (which drops sub-second
precision). The durable lesson is that ZonedDateTime offset work must stay on the
epoch-nanosecond domain: convert to ticks with floor division, read
`OffsetNanoseconds` directly from the stored model value, and for time-only
durations bypass the local PlainDateTime path entirely. Future ZonedDateTime offset
or arithmetic work should prove the transition-minus-one-nanosecond case and the
time-only fast path locally before widening.

Related ADR:
`docs/adrs/0068-keep-temporal-zoneddatetime-offsets-and-time-arithmetic-on-epoch-nanoseconds.md`.

Issue #1071 / PR #1243 fixed `String.prototype.search(undefined)` after the
first build-stage fix forced explicit `undefined` into the literal pattern
`"undefined"`. Review caught that the current spec path is
`RegExpCreate(undefined, undefined)`, which creates the same empty-pattern
behavior as an omitted argument. Future string/RegExp built-in work should let
the abstract operation receive the original `JsValue` unless the spec text has
an explicit argument-count branch, and should prove the focused
`Name=String_prototype_search` Test262 method group plus local search
regressions before claiming the fix.
