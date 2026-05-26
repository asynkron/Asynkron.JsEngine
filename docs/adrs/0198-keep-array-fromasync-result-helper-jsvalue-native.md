# ADR 0198: Keep Array.fromAsync result helper JsValue-native

## Status

Accepted

## Context

Issue `autrun-disubnwdoxb4-4df588ff17` / PR #2197 continued the recurring
object-to-`JsValue` cleanup by targeting the promise-producing
`Array.fromAsync` helper.

ADR 0111 had already moved synchronous Array static result helpers
(`Array.of`, synchronous `Array.from`, and iterable `Array.from`) to
`JsValue`, but deliberately left `ArrayFromAsync` for a separate proof because
its result boundary creates and returns a `JsPromise` object while scheduling
asynchronous iterator or array-like work.

Before this delivery, `ArrayFromAsync(...)` returned `object?` even though every
successful or rejected setup path returned the same promise object and the only
host-function callsite immediately wrapped the result with
`JsValue.FromObjectUnsafe(result)`. That made the helper boundary look like a
host/interop bridge even though it was private standard-library plumbing whose
caller already required a JavaScript value.

The accepted delivery changed `ArrayFromAsync(...)` to return `JsValue`, wrapped
`promise.JsObject` at each helper return point with
`JsValue.FromObjectUnsafe(promise.JsObject)`, and returned the helper result
directly from `ArrayConstructor.AttachFromAsync`.

Focused proof used:

```bash
rtk rg -n "internal static object\? ArrayFromAsync|ArrayFromAsync\(" src/Asynkron.JsEngine tests/Asynkron.JsEngine.Tests
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj --filter "FullyQualifiedName~AdditionalArrayMethodsTests" --configuration Release
```

The focused Array.fromAsync proof passed 45 tests. The canonical async
`run-quality` verification later passed after a mechanical warning repair in
nearby test files.

## Decision

Keep the private `Array.fromAsync` result helper `JsValue`-native.

For `ArrayFromAsync(...)` and future promise-producing Array static helper
cleanup:

1. return `JsValue` from the private helper when every setup branch returns the
   promise object for a host-function callsite that already expects `JsValue`;
2. wrap the created promise object exactly at the helper return boundary with
   `JsValue.FromObjectUnsafe(promise.JsObject)`;
3. return the helper result directly from the attached host function instead of
   re-wrapping an `object?` result at the callsite;
4. preserve promise identity, rejection behavior, iterator scheduling, and
   mapping semantics while changing only the carrier type; and
5. prove the slice with a focused signature/callsite search and the
   Array.fromAsync-owned tests before relying on wider quality gates.

## Consequences

- `Array.fromAsync` now follows the same private-result carrier principle as
  the synchronous Array static helpers while keeping its async/promise behavior
  separately owned.
- Future Array static helper migrations should not treat promise creation as a
  reason to keep an `object?` return when the helper always returns a JavaScript
  promise object to a `JsValue` host-function boundary.
- Caller-side `JsValue.FromObjectUnsafe(...)` bridges on private Array helper
  results should be treated as migration debt unless they mark an explicit
  public facade, host interop, debugger, or diagnostic boundary.
- ADR 0111 remains the decision record for synchronous Array static helpers;
  this ADR owns the async `Array.fromAsync` promise-result boundary.

## Related

- `.claude/rules/jsvalue-core-values.md`
- `docs/adrs/0111-keep-array-static-sync-result-helpers-jsvalue-native.md`
