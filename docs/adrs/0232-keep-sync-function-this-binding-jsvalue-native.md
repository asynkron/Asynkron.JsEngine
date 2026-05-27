# ADR 0232: Keep sync function this-binding JsValue-native

## Status

Accepted

## Context

Issue `autrun-ditio0qdfdmo-14faf7d798` / PR #2393 continued the
object-to-`JsValue` cleanup inside the function activation surface. The carried
build-stage signal showed three remaining matches in
`TypedAstEvaluator.SyncFunctionInvoker` for `object? initialThisValue`,
`JsValueCache.GetBoolean`, or `JsValueCache.GetNumber`.

Those matches were in the non-arrow slow invocation `this` setup. The caller
already supplied `thisValue` as a JavaScript `JsValue`, but the slow path
temporarily converted primitive receivers through boxed object carriers before
assigning the bound receiver into the function environment. That was not a
public API or host-interop boundary. It was private activation plumbing that
needed to preserve ECMAScript strict/sloppy `this` rules:

- strict calls keep the original primitive receiver;
- sloppy calls coerce nullish receivers to `globalThis`;
- sloppy primitive receivers box through the realm-specific wrapper objects;
  and
- derived constructors still rely on `super()` to initialize the receiver.

The accepted delivery migrated the selected slow-path `this` binding setup to
`JsValue` end to end, added `CoerceThisValueForNonStrict(JsValue)`, and removed
the scoped boxed boolean/number cache usage from that path. Focused proof used:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ActivationSemanticsProofPackTests.StrictAndSloppyCall_WithPrimitiveThis_PreserveCoercionRules|FullyQualifiedName~ActivationSemanticsProofPackTests.DerivedConstructor_InitializesBaseInstanceWithSuperCall|FullyQualifiedName~ActivationSemanticsProofPackTests.StrictAndSloppyCalls_KeepDistinctThisBindingRules"
rtk rg -n "object\? initialThisValue|JsValueCache.GetBoolean|JsValueCache.GetNumber" src/Asynkron.JsEngine/Ast/TypedAstEvaluator.SyncFunctionInvoker.cs
```

The focused activation proof passed, and the targeted legacy-carrier search
moved from three matches in the selected owner file to zero.

## Decision

Keep sync function `this` binding setup `JsValue`-native once the invocation
path already has a JavaScript `thisValue`.

For `TypedAstEvaluator.SyncFunctionInvoker` and adjacent function activation
helpers:

1. keep caller-supplied `thisValue`, local `boundThis` values, and
   `JsEnvironment._thisValue` assignments on `JsValue`;
2. use a `JsValue` non-strict coercion helper for nullish-to-global and
   primitive boxing semantics instead of converting through `object?`;
3. do not reintroduce `object? initialThisValue`, `object? boundThis`, or boxed
   primitive cache helpers such as `JsValueCache.GetBoolean` /
   `JsValueCache.GetNumber` in private activation `this` setup;
4. keep strict-mode calls as a no-coercion path that preserves the original
   primitive `JsValue`; and
5. prove future changes with activation proof-pack coverage for strict/sloppy
   primitive `this` and derived-constructor `super()` initialization, plus a
   scoped legacy-carrier search in `SyncFunctionInvoker`.

## Consequences

- Ordinary sync function activation no longer hides receiver values behind a
  private object-carrier bridge before storing the function environment's
  `this`.
- Future `this`-binding cleanup can target remaining activation paths without
  treating boxed primitive caches as acceptable private runtime carriers.
- Strict/sloppy receiver behavior and derived-constructor initialization remain
  the focused semantic risks for this owner surface, so they stay in
  `ActivationSemanticsProofPackTests`.
- This ADR is caused by issue `autrun-ditio0qdfdmo-14faf7d798` / PR #2393.

## Related

- `.claude/rules/function-activation-proof-pack.md`
- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0099-keep-function-activation-slot-shape-plan-owned.md`
- `docs/adrs/0101-keep-function-call-argument-carriers-typed-through-hot-paths.md`
- `docs/adrs/0230-keep-derived-class-constructor-ir-activation-super-owned.md`
