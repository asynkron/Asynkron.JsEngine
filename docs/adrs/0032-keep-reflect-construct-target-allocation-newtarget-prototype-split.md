# ADR 0032: Keep Reflect.construct target allocation and newTarget prototype split

## Status

Accepted

## Context

Issue #817 / PR #1018 fixed the focused Test262
`ReflectConstruct_ProxiedNewTargetUsesTargetRealm` regression. The failing
path involved `Reflect.construct` with an Array `target` from one realm and a
proxied `newTarget` from another realm whose `prototype` lookup produced a
non-object value.

The implementation had treated either `target` or `newTarget` being the
current realm's Array constructor as enough to select Array allocation. That
mixed two different ECMAScript roles:

- `target` decides which constructor behavior runs and whether the result is an
  Array exotic object.
- `newTarget` decides the prototype path, including the realm fallback when
  `newTarget.prototype` is not an object.

The review-back repair also exposed the opposite hazard: recognizing an Array
`newTarget` must not make an ordinary non-Array `target` allocate as an Array.
In that case the object kind stays ordinary and only the prototype fallback may
come from the `newTarget` realm.

Issue #871 / PR #1258 exposed the same split for concrete typed-array
constructors. `Reflect.construct(Int8Array, [symbolLike], newTarget)` had to
run the typed-array constructor path and reject invalid primitive length inputs
before generic construction observed `newTarget.prototype`. Treating typed-array
constructors like ordinary constructors moved prototype lookup ahead of the
typed-array argument-dispatch and `ToIndex` error boundary.

## Decision

Keep `Reflect.construct` allocation kind keyed to `target` only. Array exotic
allocation may recognize a proxied or cross-realm Array `target`, but it must
not use `newTarget` as a shortcut for choosing Array allocation.

Keep `newTarget` limited to prototype and realm selection. When
`newTarget.prototype` is not an object, fall back through the `newTarget` realm
prototype for the constructed kind, while preserving the object kind selected
by `target`.

Model future `Reflect.construct` changes as separate abstract-operation roles:
constructor target behavior first, newTarget prototype selection second, and
proxy unwrapping only where the spec role being implemented requires it.

Concrete typed-array constructors are constructor behavior selected by
`target`, not a generic prototype-resolution case. Detect them before
`newTarget.prototype` resolution so the typed-array constructor's argument
classification, primitive rejection, and allocation rules run in spec order.
Keep `%TypedArray%` itself out of the concrete typed-array path.

## Consequences

- Future construction helpers must prove both sides of the split: Array
  `target` produces Array exotic allocation, and Array `newTarget` alone does
  not change an ordinary `target` into an Array.
- Cross-realm fallback tests should assert prototype identity, not just
  `instanceof`, because realm-specific `%Array.prototype%` and
  `%Object.prototype%` are the observable result.
- Proxy handling in this area should be role-specific. Unwrapping a proxy to
  identify the `target` constructor kind must not erase revoked-proxy errors,
  constructability checks, or the separate `newTarget.prototype` lookup path.
- Typed-array `Reflect.construct` reviews must include error-order cases where
  invalid primitive arguments such as Symbol or BigInt must throw before any
  observable `newTarget.prototype` access.
- Focused proof for this class should include the exact
  `Name=ReflectConstruct_ProxiedNewTargetUsesTargetRealm` Test262 method group
  and a local non-Array-target regression. For typed-array constructor routing,
  include `Name=TypedArrayConstructors_ctors_typedarrayArg`.
- This ADR is caused by issue #817 / PR #1018 and extended by issue #871 /
  PR #1258.
