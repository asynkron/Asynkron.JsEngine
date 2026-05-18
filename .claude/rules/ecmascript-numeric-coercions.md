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
