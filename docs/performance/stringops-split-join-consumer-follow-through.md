# Stringops split/join consumer follow-through

## Scope

Issue #2150 extends ADR 0184 with a narrow follow-through slice on the existing
consumer-owned surfaces:

- `StringPrototype.SplitBySeparator` (non-empty separator path)
- `ArrayPrototype.TryJoinDenseOwnStringArray` (dense own primitive-string path)

The goal is to keep semantics and fallback behavior unchanged while tightening
consumer-side materialization/capacity work.

## Baseline signal

Command:

```bash
rtk ./benchmark.sh --allocations stringops
```

Output:

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
stringops                       330        63775.4      166     13416.2  Jint 1.99x faster      Jint 4.75x lower alloc
```

## Change

- `SplitBySeparator` now pre-counts bounded matches (`limit - 1` max) and
  allocates the result `JsArray` to exact segment count before materializing
  substrings.
- `TryJoinDenseOwnStringArray` now pre-computes the exact output length and
  initializes `StringBuilder` with that capacity after proving the dense
  primitive-string guard.

These changes do not widen the fast-path shape. Objects, sparse/prototype
cases, non-string elements, and array-like receivers continue to fall back to
the generic observable path.

## Focused semantics proof

Command:

```bash
rtk dotnet test tests/Asynkron.JsEngine.Tests/Asynkron.JsEngine.Tests.csproj -c Release --filter "FullyQualifiedName~String_Split_NonEmptySeparator_CoercesLimitBeforeSeparator|FullyQualifiedName~String_Split_NonEmptySeparator_RespectsLimit|FullyQualifiedName~Array_Join_FallsBackWhenElementToStringHasSideEffects|FullyQualifiedName~Array_Join_EmptyArrayStillCoercesSeparator|FullyQualifiedName~Array_Join_FallsBackForSparseArrayPrototypeValue|FullyQualifiedName~Array_Join_UsesPrototypeValues" -- xUnit.MaxParallelThreads=1 -timeout 20000
```

Output:

```text
ok dotnet test: 6 tests passed
```

## Final signal

Command:

```bash
rtk ./benchmark.sh --allocations stringops
```

Output:

```text
profile                 asynkron_ms    asynkron_kb  jint_ms     jint_kb  time_delta             alloc_delta
stringops                       392        63737.6      184     13416.2  Jint 2.13x faster      Jint 4.75x lower alloc
```

## Reading the result

- Allocation moved slightly down (`63775.4 KB -> 63737.6 KB`, about `37.8 KB`
  lower in this run).
- Timing moved up in this sample (`330 ms -> 392 ms`), so this slice should be
  treated as allocation-focused with noisy timing rather than a timing win.
