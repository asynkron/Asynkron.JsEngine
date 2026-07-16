# Async Iteration Helper Allocation Evidence

Date: 2026-06-08

## Slice

This slice isolates the top-level-await `for await...of` bridge path that calls
`IterationHelper.GetAsyncIteratorCore` and `IterationHelper.IteratorNextCore`
after an awaited iterable has resolved.

`IterationHelper` remains the owner of the async iterator protocol helper
behavior. The `JsEngine` bridge is only a caller that supplies the already
resolved iterable and iterator values.

This is a narrow protocol allocation cleanup. It does not claim full async,
module, or runtime parity.

## Baseline Signal

Baseline timestamp: 2026-06-08T18:35:16Z
Baseline signal: direct async-iteration helper argument-array call sites = 2

Command:

```bash
rtk rg -n "IterationHelper\.(GetAsyncIteratorCore|IteratorNextCore)\(\[" src/Asynkron.JsEngine/JsEngine.cs
```

Output:

```text
7151:                        var iteratorValue = IterationHelper.GetAsyncIteratorCore([resolved], _engine);
7158:                        var nextResult = IterationHelper.IteratorNextCore([iteratorValue], _engine);
```

The two inline collection expressions allocated one argument array each before
entering the protocol helpers.

## Final Signal

Final timestamp: 2026-06-08T18:38:15Z
Final signal: direct async-iteration helper argument-array call sites = 0
Signal delta: -2 direct argument-array allocation sites on the selected bridge
path.

Command:

```bash
rtk rg -n "IterationHelper\.(GetAsyncIteratorCore|IteratorNextCore)\(\[" src/Asynkron.JsEngine/JsEngine.cs
```

Output:

```text
<no matches>
```

## Change

`IterationHelper` now exposes direct `JsValue` core overloads for:

- `GetAsyncIteratorCore(JsValue iterable, JsEngineInstance engine)`
- `IteratorNextCore(JsValue iteratorValue, JsEngineInstance engine)`

The registered host helpers still accept `IReadOnlyList<JsValue>` and delegate
to the same core logic, so host-function argument validation and error behavior
stay in the existing entrypoint shape. The `JsEngine` top-level-await bridge now
calls the direct overloads and avoids allocating one-element argument arrays
before the protocol helpers.

## Verification

Focused semantic proof ran the existing top-level-await async-iteration test
and two adjacent async-iterator protocol tests:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests --filter "FullyQualifiedName~ModuleTests.TopLevelAwait_ForAwaitOfAwaitedIterableAndBodyUsesAsyncIteratorPath|FullyQualifiedName~AsyncIterationTests.ForAwaitOf_WithCustomAsyncIterator|FullyQualifiedName~AsyncIterationTests.ForAwaitOf_WithCustomSyncAsyncIterator" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Output:

```text
ok dotnet test: 3 tests passed, 7 warnings in 1 projects (12.6 s)
```

The warnings were pre-existing nullable warnings in unrelated test files.
