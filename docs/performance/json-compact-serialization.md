# JSON compact serialization optimization

Issue: `autrun-diqzs4qq1p5s-4e9bf90752`

## Baseline

The automation baseline was captured with:

```bash
rtk ./benchmark.sh
```

`json` was the largest current loser in that run:

```text
json                           4728      848  Jint 5.58x faster
```

The selected profile was captured before editing with:

```bash
rtk ./tools/profile json --cpu --calltree-depth 40 --calltree-width 40
```

The CPU profile pointed at `JsonHelper` as the bounded owner surface:

```text
JsonHelper.ParseJsonValue              103.92 ms
JsonHelper.SerializeJsonProperty        90.94 ms
JsonHelper.ParseJsonWithReviverJsValue  62.40 ms
JsonHelper.Stringify                    48.22 ms
JsonHelper.SerializeJsonObject          44.11 ms
JsonHelper.SerializeJsonArray           42.71 ms
```

## Change

The compact `JSON.stringify` path now streams object members and array elements
directly into a `StringBuilder` instead of first collecting intermediate member
strings into a `List<string>` and then joining them.

The no-reviver `JSON.parse` array path also skips per-element index string
creation. Those keys are only needed for the reviver source-text tracker, so the
common parse path now passes null parent metadata instead.

The generic fallback behavior is unchanged: replacers, `toJSON`, raw JSON,
pretty-printing, proxy-aware key enumeration, circular checks, and reviver source
tracking still use the same semantic paths.

## Final signal

Post-change `json` runs:

```bash
rtk ./benchmark.sh json
```

```text
json                           2471      840  Jint 2.94x faster
json                           1335      780  Jint 1.71x faster
```

Warm repeated timing without rebuilding the runner:

```bash
rtk ./tools/compare-jint-profiles --no-build json
```

```text
json                           1350      652  Jint 2.07x faster
json                           1343      757  Jint 1.77x faster
json                           1347      735  Jint 1.83x faster
```

The selected benchmark improved by more than 10% versus the `4728 ms` baseline
in all post-change measurements.

## Verification

```bash
rtk dotnet build tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release -v minimal
```

Result: passed with existing warnings.

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~FoundationTests&FullyQualifiedName~Json" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Result: 6 tests passed.
