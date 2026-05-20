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
    the current realm's Array constructor.
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
24. For `Temporal.PlainDate.prototype.until` and adjacent PlainDate difference
    work, matching BCL-backed non-ISO calendars must compute month and year
    largest-unit differences in calendar space, not through ISO month/year
    arithmetic over the stored ISO projection. Convert endpoints to
    calendar-visible fields before balancing months or years, keep day/week
    largest-unit behavior on elapsed ISO dates unless the spec path says
    otherwise, and resolve PlainDate property-bag `monthCode` values through the
    resolved calendar/year for leap-month-aware calendars.
25. For `Temporal.PlainMonthDay.from` property bags, keep ISO field reads in
    observable spec order and keep era reads calendar-dependent. ISO bags must
    read `calendar`, `day`, `month`, `monthCode`, `year`, then options; they
    must not observe `era` or `eraYear` getters. Non-ISO paths may read
    `era`/`eraYear` for validation, but should read them once and reuse the
    observed presence instead of re-reading later.
26. For `Temporal.PlainMonthDay.prototype.toLocaleString` and adjacent
    `Intl.DateTimeFormat` Temporal formatting, keep calendar-visible fields
    separate from reference ISO slots. Use `ReferenceYear`, `ReferenceMonth`,
    and `ReferenceDay` when constructing host date or epoch values for
    PlainMonthDay; use the receiver calendar to format that reference date, not
    to supply ISO constructor fields. PlainMonthDay `dateStyle` expansion must
    also filter to month/day fields for that Temporal kind instead of leaking a
    year field through shared date-style defaults.
27. For `Temporal.PlainMonthDay.prototype.toString`, treat `calendarName` as
    calendar-annotation policy, not as a request to collapse all calendars to
    the ISO short month-day shape. `calendarName: "never"` must keep ISO
    receivers as `MM-DD`, but non-ISO receivers must return the non-annotated
    reference ISO date `YYYY-MM-DD`. Keep `auto`, `always`, and `critical`
    behavior on their existing annotation branches unless the spec path proves
    a separate change.
28. For `Temporal.PlainTime.from` property bags, keep leap-second and
    out-of-range time fields on the shared Temporal overflow path. Default and
    `overflow: "constrain"` calls must normalize `second: 60` and higher
    seconds to the valid maximum second, while `overflow: "reject"` must throw
    `RangeError` at the time-range validation point.
29. For `Temporal.PlainYearMonth.prototype.toLocaleString` and adjacent
    `Intl.DateTimeFormat` Temporal formatting, keep date-style expansion in the
    field domain of PlainYearMonth. `dateStyle` may format year/month but must
    not leak the reference day, and non-ISO month names must come from the
    receiver calendar's year/month domain rather than a Gregorian
    `DateTimeOffset` month.

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
