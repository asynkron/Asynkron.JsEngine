# ECMAScript Numeric Coercions

When implementing ECMAScript numeric storage or conversion algorithms, funnel
the conversion through shared helpers that encode the spec operation instead of
open-coding casts at individual call sites.

## Rules

1. For DataView and typed-array setters, apply the specified argument coercion
   sequence before writing bytes: index conversion (`ToIndex` where the spec
   requires it), value conversion (`ToNumber` or the required BigInt path), then
   the target element conversion such as `ToInt16` or `ToUInt16`.
2. Update every reachable entry point for the same JavaScript operation. If a
   prototype method and a direct host-facing `JsDataView`/typed-array method can
   perform the same operation, keep them semantically aligned.
3. Prefer a named conversion helper in `JsNumericConversions` when the operation
   has wrapping, modulo, signed-zero, NaN, or infinity behavior. Do not replace
   the helper with `(short)(int)`, `(byte)`, or similar casts unless the exact
   ECMAScript operation has already been encoded by the helper.
4. Add focused coverage for edge values from Test262 conversion tables when a
   setter or conversion helper changes. Include return-value behavior if the
   Test262 case checks it.
5. If a Test262 method group already passes before a suspected numeric setter
   repair, do not invent runtime churn just to satisfy the incident. Add or
   keep a small internal regression that captures the conversion table,
   observable storage result, and return value so the behavior stays pinned in
   the faster local suite.
   For typed-array integer-indexed `[[Set]]`, preserve value coercion as
   observable work before range or detached-buffer validity can turn the write
   into a no-op. Pin both Number and BigInt typed-array paths when a regression
   involves throwing `valueOf`, `Symbol.toPrimitive`, or buffer detachment
   during coercion.
6. For Math, Number, and other numeric built-ins with signed-zero semantics,
   decide explicitly whether the operation must preserve `-0` or normalize it
   to `+0`. Plain equality cannot prove the sign; pin the behavior with
   `Object.is(...)` or reciprocal-infinity checks such as `1 / result`.
7. For host math wrappers, preserve the ECMAScript argument coercion order
   before applying any signed-zero correction. Do not assume `System.Math` or
   another host library matches every JavaScript signed-zero quadrant; check the
   spec-visible result at the built-in boundary.
8. For binary16/float16 math built-ins, preserve the ECMAScript special-value
   order before delegating to host `Half` conversion. In particular, signed zero
   must be returned before binary16 conversion so host casting cannot erase the
   JavaScript-observable sign.
9. For 32-bit integer math built-ins such as `Math.imul`, follow the exact
   abstract operation sequence from the spec. `ToUInt32` operands, unchecked
   modulo-2^32 arithmetic, and the final signed Int32 interpretation are not
   interchangeable with multiplying `ToInt32` operands or relying on host
   overflow behavior.
10. For aggregate numeric built-ins such as `Math.hypot`, track all-zero finite
    inputs explicitly when the spec requires canonical positive zero. Preserve
    the surrounding special-value order, especially Infinity before NaN, and pin
    all `+0`/`-0` argument combinations with reciprocal-infinity assertions.
11. For `JSON.parse` number materialization, do not rely on the parsed `double`
    alone when zero sign is observable. Inspect the raw JSON number text only
    for zero-valued tokens, or when reviver `context.source` tracking needs the
    original lexeme, so signed-zero semantics are preserved without adding
    per-number raw-text allocations to the common parse path.
12. For `Map` and `Set` collection-key storage, canonicalize numeric `-0` to
    `+0` at the shared key-extraction boundary, not only in lookup equality.
    `SameValueZero` lookup compatibility does not prove the stored insertion key
    is spec-visible as positive zero through `keys()`, `entries()`, or
    `Map.groupBy`; pin grouped and direct collection cases with `Object.is(...)`
    or reciprocal-infinity assertions.
13. For aggregate Math built-ins, keep each operation's empty-input and
    all-zero rules separate. `Math.hypot` all-zero inputs canonicalize to `+0`,
    while `Math.sumPrecise` has an empty-iterable identity of `-0` after the
    required NaN/Infinity handling. Pin these cases with SameValue-style
    assertions and the owning Test262 method group instead of reusing ordinary
    numeric equality or another aggregate's zero policy.
14. For `Intl.NumberFormat` string inputs, preserve decimal-string lexeme
    precision before generic `ToNumeric`/`double` fallback. Keep coefficient
    size and positive-exponent materialization bounded, but allow very large
    positive decimal scale for scientific or engineering notation when the
    formatter can preserve the exponent without building the full fixed decimal.
    Pin parser cap boundaries such as `1e-1001` with focused Intl regressions
    and the owning Test262 method group.
15. For `Intl.NumberFormat` range helpers, route both endpoints through the
    same Intl-owned numeric formatting path used by `formatRange` before
    composing either the final string or `formatRangeToParts` output. Do not let
    `formatRangeToParts` reintroduce generic `double` conversion or a separate
    endpoint formatter that loses decimal-string precision.
16. For `Intl.DurationFormat` fractional unit output, combine seconds,
    milliseconds, microseconds, and nanoseconds as exact decimal quantities
    before formatting. Do not aggregate sub-second units through `double`
    arithmetic and then hand the rounded binary value to the number formatter.
    Preserve the sign separately from the absolute exact magnitude so
    sign-display behavior stays observable while fractional precision remains
    exact.
17. For `Temporal.Duration` ISO string formatting, keep millisecond,
    microsecond, and nanosecond magnitudes as exact integer quantities until
    subsecond balancing is complete. Do not cast large subsecond components to
    `long` or aggregate them through `double` before balancing into seconds.
    Preserve sign separately from absolute magnitude and prove large exact
    object-bag cases with the focused `Name=Temporal_Duration_from` Test262
    method group.
18. For `Temporal.Duration.prototype.total` with a ZonedDateTime `relativeTo`
    and calendar units (`week`, `month`, or `year`), keep the fractional
    denominator as the positive span between adjacent whole-unit boundaries.
    Preserve the sign on the remainder between the actual end instant and the
    threshold boundary; do not divide a negative remainder by a negative
    backward boundary span. Pin negative partial totals locally for all three
    calendar units before relying on the focused
    `Name=Temporal_Duration_prototype_total` Test262 method group.
19. For `Object.defineProperty`/`Reflect.defineProperty` on typed-array
    integer-indexed keys, preserve the `IntegerIndexedElementSet` order:
    validate the target index, coerce the descriptor value through `ToNumber`
    or `ToBigInt`, then perform the second validity check before storing. If
    the value coercion detaches the receiver buffer, the operation returns
    `true` with no visible write; do not move the detached-buffer check ahead
    of observable value conversion. Pin both number and BigInt typed arrays,
    wrapping behavior, and the detach-during-`valueOf` case with local
    regressions plus the focused
    `Name=TypedArrayConstructors_internals_DefineOwnProperty` Test262 method
    group.

## Why

Issue #760 / PR #914 fixed `DataView.prototype.setInt16` Test262 failures after
one path used spec-shaped `ToNumber`/`ToInt32` logic while the direct
`JsDataView` method still used raw `TryGetDouble` and C# casts. Issue #763 /
PR #916 repeated the same class of bug for unsigned 16-bit conversion:
`setUint16` needed a shared `ToUInt16` helper, prototype-path use, and
host-facing `JsDataView` fallback parity. Issue #765 / PR #918 confirmed the
same guard shape for `setUint8`: the exact Test262 group was already green, so
the correct learnable action was an internal regression covering Uint8 modulo
conversion, `undefined` return, and `Uint8Array` readback over the same buffer,
not a runtime rewrite.

The durable lesson is that numeric setter semantics are shared runtime behavior,
not a single prototype-method detail. Open-coded casts make it easy for signed
zero, unsigned modulo wrapping, large integer wrapping, NaN, infinity, and
return-value checks to drift between entry points. Passing Test262 today is also
not enough when the issue exposes a previously unpinned edge: keep the local
guard so future refactors see the failure quickly.

Issue #874 / PR #1233 repeated the green-on-main closeout pattern for
`TypedArrayConstructors_internals_Set`, but exposed a narrower typed-array
`[[Set]]` ordering invariant. The focused Test262 method group already passed
52/52, so the delivery stayed test-only and added regressions proving that a
throwing `valueOf` propagates before out-of-range writes or detached-buffer
writes can be treated as no-ops, for both `Int8Array` and `BigInt64Array`.
Future typed-array setter refactors must keep coercion order observable across
Number and BigInt element paths instead of moving validity checks ahead of
`ToNumber` or `ToBigInt`.

Issue #797 / PR #922 fixed `Math.abs(-0)`: the built-in must return positive
zero under SameValue semantics, while nearby operations such as `Math.round`
preserve negative zero in selected cases. The durable rule is to make signed
zero policy explicit at the built-in boundary and test it with an observable
sign check; `== 0` and ordinary numeric equality erase the distinction that
Test262 asserts.

Issue #798 / PR #923 repeated the signed-zero lesson for `Math.atan2(-0, +0)`.
The fix still had to run `ToNumber(y)` before `ToNumber(x)`, then override the
host `Math.Atan2` result only for the spec-required negative-zero quadrant. That
recurrence is why Math built-ins need both an explicit signed-zero policy and a
proof that host-library delegation has not erased JavaScript-observable zero
signs.

Issue #799 / PR #925 repeated the same boundary problem for `Math.f16round`.
The built-in still needs normal `ToNumber` coercion and binary16 rounding, but
the spec's signed-zero special case must be handled before delegating to
`System.Half`; otherwise a host cast can turn a required `-0` result into `+0`.
Pin this with both focused internal coverage and the exact Test262 method group
when a Math built-in depends on host numeric conversion.

Issue #801 / PR #928 fixed `Math.imul` after signed Int32 operands made simple
cases pass while failing modulo multiplication edges such as
`0xffffffff * 5`, `2147483647 * 2147483647`, and `65535 * 65535`. The durable
lesson is that 32-bit built-ins need the spec's unsigned modulo domain before
the signed return interpretation; local coverage should pin the boundary table
alongside the focused Test262 method group.

Issue #800 / PR #927 fixed `Math.hypot` after Test262 exposed that every finite
all-zero argument list, including mixed `+0` and `-0`, must return canonical
`+0`. The fix kept the existing Infinity-before-NaN behavior and added focused
reciprocal-infinity assertions so a future refactor cannot hide the zero sign
behind ordinary numeric equality.

Issue #792 / PR #982 fixed `JSON.parse("-0")` after `JsonElement.GetDouble()`
erased the negative-zero spelling before constructing the JavaScript value. The
durable rule is to treat the original JSON number lexeme as semantic data for
zero sign and reviver source tracking, while keeping `GetRawText()` targeted so
large nonzero numeric arrays do not pay avoidable string-allocation cost.

Issue #796 / PR #987 fixed `Map.groupBy` negative-zero keys after map lookup
equality accepted both `0` and `-0` but the stored insertion key could still
expose `-0` during iteration. The durable rule is that collection-key
extraction owns `-0` to `+0` canonicalization for Map/Set storage; individual
grouping or prototype methods should not grow separate signed-zero patches that
let stored-key representation drift from lookup semantics.

Issue #802 / PR #995 fixed `Math.sumPrecise([])` after the empty finite-value
path reused the default positive-zero return even though the operation's empty
iterable identity is negative zero. The recurrence is why aggregate Math
built-ins should not share a single "all zeros" intuition: the special-value
order and exact aggregate identity are operation-specific, and the proof needs
both an internal `Object.is(..., -0)` regression and the focused
`Name=Math_sumPrecise` Test262 group.

Issue #807 / PR #1001 fixed `Intl.NumberFormat.prototype.format` after
decimal-string inputs were routed through generic string-to-`double`
conversion. That lost mathematical precision for decimal-string values and, at
the exact parser cap boundary, turned tiny nonzero scientific values such as
`1e-1001` into `0`. The durable rule is that Intl owns decimal-string lexeme
preservation before fallback: keep exact coefficient and positive-exponent
growth bounded, but let scientific and engineering notation carry large
negative exponents without materializing enormous fixed decimal strings.

Issue #808 / PR #1004 fixed the same decimal-string boundary for
`Intl.NumberFormat.prototype.formatRange` and `formatRangeToParts`. The range
parts helper had to use the same `FormatNumericForRange` endpoint path as the
string helper so a range such as `"987654321987654321"` to
`"987654321987654322"` stays precise across both surfaces. Future range work
should prove both focused Test262 method groups and a local regression that
compares the joined parts with the formatted range when the same endpoint
formatting should be observable.

Issue #1025 / PR #1097 fixed `Intl.DurationFormat.prototype.format` after
fractional unit combination used binary `double` arithmetic for seconds,
milliseconds, microseconds, and nanoseconds. Test262 required exact
mathematical values before the formatter applied truncation and padding, so the
durable rule is that DurationFormat owns exact decimal aggregation before
calling the Intl number formatter. Future work should pin this with local
DurationFormat regressions and the focused
`Name=DurationFormat_prototype_format` Test262 method group.

Issue #833 / PR #1129 fixed `Temporal.Duration.from` exact numerical object-bag
cases near the `2**53` seconds boundary after `Temporal.Duration` string
formatting cast large millisecond, microsecond, and nanosecond magnitudes to
`long` before balancing. The durable rule is that Temporal duration ISO
formatting owns exact subsecond integer aggregation until the value has been
balanced into bounded seconds and fractional nanoseconds. This complements the
Intl DurationFormat rule but applies to `Temporal.Duration.prototype.toString`
and `Temporal.Duration.from(...).toString()` rather than Intl number
formatting.

Issue #835 / PR #1133 fixed `Temporal.Duration.prototype.total` after the
ZonedDateTime calendar-unit fractional path preserved variable calendar spans
but let the denominator inherit the direction of a negative duration. A
negative one-day total relative to the Unix epoch must be `-1 / 7` weeks,
`-1 / 31` months, and `-1 / 365` years, not the corresponding positive
fractions. The durable rule is that the adjacent-boundary span is a positive
measurement, while the signed remainder carries the result direction.

Issue #873 / PR #1228 pinned the typed-array
`IntegerIndexedElementSet` value-conversion order for
`Object.defineProperty`. The focused Test262 group was already green, so the
delivery stayed test-only, but it captured the edge that future refactors are
likely to break: `ToNumber`/`ToBigInt` and target element wrapping are
observable before the second valid-index check, and a value coercion that
detaches the receiver buffer still returns `true` without exposing a write.

Related ADR:
`docs/adrs/0051-keep-temporal-duration-calendar-total-fractions-signed.md`.
