# ADR 0182: Keep module namespace own keys JsValue-native

## Status

Accepted

## Context

Issue `autrun-disnitlzqrkw-d2a93ff0bf` / PR #2135 continued the bounded
Unboxer cleanup of legacy `object?` carriers inside the core runtime.

Before the delivery, `ModuleNamespace.OwnKeys()` returned
`IEnumerable<object?>` even though the sequence represented ECMAScript property
keys: sorted export string names plus the namespace object's
`Symbol.toStringTag` key. The two direct standard-library consumers then had
to recover the JavaScript value shape themselves. `Reflect.ownKeys` wrapped
each key with `JsValue.FromObjectUnsafe(...)`, and
`Object.getOwnPropertySymbols` pattern-matched the raw object carrier as a
`JsSymbol`.

That object carrier was not a public facade, host-interop, debugger, or
diagnostic boundary. It was a private module namespace key enumeration feeding
standard-library APIs that already return JavaScript values and must preserve
both string and symbol property keys.

The accepted delivery changed `ModuleNamespace.OwnKeys()` to return
`IEnumerable<JsValue>`. `Reflect.ownKeys` now constructs the result array from
that typed sequence directly, and `Object.getOwnPropertySymbols` filters the
same sequence with `JsValue.TryGetSymbol`. Focused module tests assert that
`Reflect.ownKeys(ns)` includes export string keys and
`Symbol(Symbol.toStringTag)`, while `Object.getOwnPropertySymbols(ns)` returns
only `Symbol(Symbol.toStringTag)`.

## Decision

Keep module namespace own-key enumeration `JsValue`-native.

For module namespace and similar private property-key enumeration helpers:

1. represent keys as `JsValue` when the consumer is a JavaScript reflection or
   object API;
2. preserve string and symbol keys through the owner helper instead of making
   every caller reconstruct property-key semantics;
3. avoid `IEnumerable<object?>`, caller-side `JsValue.FromObjectUnsafe(...)`,
   or raw `JsSymbol` pattern matching as the private key-carrier contract;
4. keep `Symbol.toStringTag` as an actual symbol key for `Reflect.ownKeys` and
   `Object.getOwnPropertySymbols`, while the string-key `Keys` view can remain
   a separate compatibility/projection surface; and
5. prove future migrations with a scoped legacy-signature search plus focused
   coverage for both `Reflect.ownKeys` and `Object.getOwnPropertySymbols`.

## Consequences

- Future module namespace reflection changes should not reintroduce
  `IEnumerable<object?>` or generic object wrapping on the own-key path.
- A string-only keys projection is not a substitute for `[[OwnPropertyKeys]]`;
  reflection APIs need a typed property-key sequence that carries symbols.
- The focused proof for this area should cover both the mixed string/symbol
  result from `Reflect.ownKeys` and the symbol-only filter from
  `Object.getOwnPropertySymbols`.
- This decision complements the broader module namespace construction boundary
  in ADR 0091 and the core `JsValue` migration policy.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `.claude/rules/ecmascript-modules.md`
- `docs/adrs/0091-keep-module-namespace-construction-resolution-lazy.md`
