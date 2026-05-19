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
