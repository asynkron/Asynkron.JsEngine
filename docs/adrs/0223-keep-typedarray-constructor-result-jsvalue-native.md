# ADR 0223: Keep typed-array constructor results JsValue-native

## Status

Accepted

## Context

Issue `autrun-dit9qcqe3mzs-470462a366` / PR #2317 continued the recurring
object-to-`JsValue` cleanup by targeting the shared typed-array constructor
result helper.

Before the delivery, `TypedArrayHelper.CreateTypedArrayConstructor<T>` used a
local `ConstructTypedArray(...)` helper that returned `object?`. The attached
host constructor immediately wrapped that result with:

```csharp
JsValue.FromObjectUnsafe(ConstructTypedArray(args, newTarget))
```

Every helper branch actually returned the same concrete typed-array subtype
`T`: zero-length construction, construction from an existing typed array,
construction from an `ArrayBuffer`, and construction from array-like or iterable
inputs. The helper also owned the `newTarget` prototype resolution before
returning the target. `JsValue` already has a `TypedArrayBase` object
conversion, so the `object?` return type only preserved an avoidable
object-carrier bridge at the constructor result boundary.

The accepted delivery changed `ConstructTypedArray(...)` to return `T` and
returned the helper result directly from the host-function invoke path.

Focused proof used:

```bash
rtk rg -n "object\?\s+ConstructTypedArray|FromObjectUnsafe\(ConstructTypedArray" src/Asynkron.JsEngine/StdLib/TypedArray/TypedArrayHelper.cs
rtk dotnet build src/Asynkron.JsEngine/Asynkron.JsEngine.csproj
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~TypedArray"
```

The final legacy-signature/rewrap search had no matches, the engine project
build passed with 0 warnings/errors, and the focused typed-array test pack
passed 192/192.

## Decision

Keep shared typed-array constructor result helpers typed once every branch
returns a concrete typed-array object.

For `TypedArrayHelper.CreateTypedArrayConstructor<T>` and adjacent typed-array
constructor helpers:

1. prefer returning the concrete `TypedArrayBase` subtype `T` or `JsValue`
   instead of `object?` when the host constructor callsite already needs a
   JavaScript value;
2. return the helper result directly from `SetInvokeWithContext` and rely on
   the typed `TypedArrayBase` to `JsValue` conversion rather than caller-side
   `JsValue.FromObjectUnsafe(...)` wrapping;
3. preserve `newTarget` validation, prototype resolution, and target identity
   inside the shared helper; and
4. keep from-length, from-buffer, from-typed-array, from-array-like, and
   iterable construction branches under one proof boundary unless a future
   behavior change proves a branch-specific split.

## Consequences

- Typed-array constructor internals no longer route the constructed runtime
  object through an untyped `object?` result carrier.
- Future typed-array constructor work should not reintroduce
  `object? ConstructTypedArray(...)` or
  `JsValue.FromObjectUnsafe(ConstructTypedArray(...))` in the private
  constructor path.
- Prototype and `newTarget` behavior remain constructor-helper owned; result
  carrier cleanup must not split those semantics across per-branch callsites.
- The right proof shape is a focused legacy-signature/rewrap search paired with
  typed-array constructor tests before widening to broader typed-array method
  behavior.
- This ADR is caused by issue `autrun-dit9qcqe3mzs-470462a366` / PR #2317.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0084-keep-typedarray-slice-species-result-validation.md`
- `docs/adrs/0111-keep-array-static-sync-result-helpers-jsvalue-native.md`
- `docs/adrs/0198-keep-array-fromasync-result-helper-jsvalue-native.md`
