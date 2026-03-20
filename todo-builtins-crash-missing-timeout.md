# Investigation Report: BuiltIns Tests Crash Test Host (OOM/StackOverflow)

## Problem Summary
Approximately 50+ built-in function Test262 tests across diverse categories (Array, Object, Number, String, BigInt, Reflect, decodeURI, encodeURI, parseInt, parseFloat) reportedly crash the dotnet test host when running as part of the full BuiltIns suite. Individual test execution succeeds for all of them. The root cause is **missing ExecutionTimeout on the BaseRealmSnapshot code path**, allowing computationally heavy tests to run unbounded when executed in parallel, ultimately causing OOM that kills the process and takes out co-scheduled lightweight tests as collateral damage.

## Affected Components
- `tests/Asynkron.JsEngine.Tests.Test262/BaseRealmSnapshot.cs:63-66` -- sets `ExecutionTimeout = null`
- `tests/Asynkron.JsEngine.Tests.Test262/Test262Test.cs:204-215` -- snapshot vs non-snapshot path divergence
- `tests/Asynkron.JsEngine.Tests.Test262/Generated/Tests262Harness.Test262Test.generated.cs:63` -- `[Parallelizable(ParallelScope.All)]`
- `src/Asynkron.JsEngine/JsEngine.cs:339,565-571` -- ExecutionTimeout property and CancellationToken creation

## Evidence Collected

### Individual Test Execution: All Pass
Every single test listed by the user passes when run individually or in small batches:
```
dotnet test --filter "FullyQualifiedName~S15.1.3.1_A2.5_T1"  --> PASS (30 seconds)
dotnet test --filter "FullyQualifiedName~15.2.3.6-4-102"       --> PASS (< 1 second)
dotnet test --filter "FullyQualifiedName~toSorted/name"         --> PASS (< 1 second)
dotnet test --filter "FullyQualifiedName~EPSILON"               --> PASS (< 1 second)
```

### Heavy Tests Take 30+ Seconds Without Timeout
The URI decoding tests with 4-level nested loops (A2.5 tests) take ~30 seconds each:
- `S15.1.3.1_A2.5_T1.js` (decodeURI, 4-nested loop over UTF-8 byte ranges): **30s per instance**
- `S15.1.3.2_A2.5_T1.js` (decodeURIComponent, same pattern): **30s per instance**
- `S15.1.3.1_A1.12_T2/T3.js` (decodeURI, 0x00-0xFFFF loop): **7s each**
- `S15.1.3.4_A2.3_T1.js` (encodeURIComponent, 0x0800-0xD7FF loop): **5s each**
- `S15.1.2.3_A6.js` (parseFloat, 0-65535 loop): **1s each**
- `15.4.4.20-9-c-ii-1.js` (filter on sparse array with 1M length): **1s each**

### Timeout Discrepancy Between Code Paths

**Non-snapshot path** (Test262Test.cs:212-215):
```csharp
new JsEngine(...) { ExecutionTimeout = TimeSpan.FromSeconds(10) };
```

**Snapshot path** (BaseRealmSnapshot.cs:63-66, used by default):
```csharp
var engine = new JsEngine(options, skipStdLibInitialization: true)
{
    ExecutionTimeout = null,  // <-- NO TIMEOUT
};
```

When `ExecutionTimeout = null`, `CreateEvaluationCancellationToken()` returns `CancellationToken.None`, and the backward-jump cancellation check in `TypedAstEvaluator.ExecutionPlanRunner.Loop.cs:241` becomes a no-op:
```csharp
if (target <= _programCounter)
{
    context.ThrowIfCancellationRequested(); // no-op with CancellationToken.None
}
```

### Memory Usage Under Load
Running the full BuiltIns suite (44,533 test cases) with only 4 workers, the test host consumes **1.4 GB RSS** within minutes. With default NUnit parallelism (all cores, typically 8-12), memory pressure is even higher.

### Crash Pattern from Sequence Files
Existing sequence files in TestResults/ show the tests that were ACTIVE when the host process died in prior runs. These were: `RegExp_propertyEscapes_generated`, `String_prototype_matchAll`, `Map`, and `Temporal` tests -- all heavy computation tests. The tests listed by the user were likely co-scheduled and killed as collateral.

### Test Categories Breakdown

**Truly heavy tests (the primary OOM contributors):**
1. decodeURI `S15.1.3.1_A2.5_T1.js` -- 4-level nested loop, ~100K+ iterations, 30s runtime
2. decodeURIComponent `S15.1.3.2_A2.5_T1.js` -- same pattern, 30s runtime
3. decodeURI `S15.1.3.1_A1.12_T2/T3.js` -- loop 0x00-0xFFFF calling `String.fromCharCode`, 7s each
4. decodeURI `S15.1.3.1_A1.2_T1/T2.js` -- loop 0x00-0xFFFF, 4s each
5. decodeURIComponent `S15.1.3.2_A1.2_T1/T2.js` -- loop 0x00-0xFFFF, 4s each
6. encodeURIComponent `S15.1.3.4_A2.3_T1.js` -- loop 0x0800-0xD7FF, 5s each
7. parseFloat `S15.1.2.3_A6.js` -- loop 0-65535, 1s each
8. parseInt `S15.1.2.2_A7.2_T1.js`, `S15.1.2.2_A7.3_T1/T2.js` -- triple nested loop
9. filter `15.4.4.20-9-c-ii-1.js` -- sparse array with length 1,000,000
10. String.prototype.indexOf `S15.5.4.7_A5_T6.js` -- loop 0x0020-0x007D

**Lightweight tests (collateral damage, always pass individually):**
- Array.prototype.every ToPrimitive tests: `15.4.4.16-3-20/21/22.js`
- Array.prototype.filter basic tests: `15.4.4.20-9-c-ii-4/5/23.js`
- Array.prototype.flat: `symbol-object-create-null-depth-throws.js`
- Array.prototype.toSorted: `name.js`, `zero-or-one-element.js`
- Array.prototype.toSpliced: `deleteCount-clamped*.js`, `deleteCount-missing.js`
- Object.defineProperty: `15.2.3.6-4-102` through `15.2.3.6-4-106`, `15.2.3.6-4-540-3` through `540-10`
- Number: `EPSILON.js`, `MIN_SAFE_INTEGER.js`, `parseFloat.js`, `parseInt.js`, `prop-desc.js`, `proto-from-ctor-realm.js`
- String.prototype.toUpperCase: `S15.5.4.18_A10.js` through `S15.5.4.18_A6.js`
- BigInt.asIntN: `bigint-tobigint-errors.js` through `bits-toindex-toprimitive.js`
- Reflect.deleteProperty: `name.js` through `return-boolean.js`
- String.prototype.indexOf: `searchstring-tostring-errors.js`

## Root Cause Analysis

### Hypothesis 1 (Most Likely): Missing ExecutionTimeout in Snapshot Path Causes OOM Under Parallel Load

The `BaseRealmSnapshot.CreateEngine()` explicitly sets `ExecutionTimeout = null`, removing all timeout protection. The non-snapshot path correctly sets `ExecutionTimeout = TimeSpan.FromSeconds(10)`. With 44K+ tests running with `[Parallelizable(ParallelScope.All)]`, multiple heavy tests (URI, parseInt, parseFloat loops running 30+ seconds each) execute simultaneously with no timeout. The cumulative memory from multiple unbounded engines exceeds available RAM, causing OOM that kills the test host process.

The lightweight tests (propertyHelper.js tests, ToPrimitive tests, etc.) are innocent bystanders killed when the process dies.

- Evidence supporting:
  - All tests pass individually (no intrinsic crash)
  - Heavy tests take 30s with no timeout in snapshot path
  - Test host reaches 1.4GB RSS with only 4 parallel workers
  - Existing crash sequence files show heavy tests (RegExp, matchAll) as the active tests at crash time
  - The non-snapshot path correctly has a 10-second timeout
  - `BaseRealmSnapshot.UseSnapshot` defaults to `true`, so almost all test runs use the no-timeout path

- Evidence against: None found -- all evidence supports this hypothesis.

**Live reproduction:** Running the full BuiltIns suite with `--blame-crash -- NUnit.NumberOfTestWorkers=4` during this investigation, the test host process (PID 42600) was killed after reaching 1.4GB RSS. Only 80 lines of output were produced before the process died. The blame-crash collector created empty result directories (833b230e, 01d8b0ec, b8307d99) with no sequence files or dumps -- the process was killed before it could write crash data. This is consistent with OOM kill.

### Hypothesis 2: CompareArrayPatchScript Adds Per-Test Overhead

Every test executes the `CompareArrayPatchScript` which runs `new Intl.Locale('en').getWeekInfo()`, `Reflect.ownKeys()`, and `JSON.stringify()`. For 44K tests, this adds significant cumulative CPU and memory overhead. While not a crash cause by itself, it amplifies memory pressure.

- Evidence supporting: The script creates objects for every test
- Evidence against: It runs successfully in all individual test cases

## Recommended Fix

### Option A: Set ExecutionTimeout in BaseRealmSnapshot.CreateEngine (Recommended)

In `BaseRealmSnapshot.cs`, change `CreateEngine` to set a timeout:

```csharp
internal JsEngine CreateEngine(IJsEngineOptions? options = null)
{
    var engine = new JsEngine(options, skipStdLibInitialization: true)
    {
        ExecutionTimeout = TimeSpan.FromSeconds(10), // Match non-snapshot path
    };
    // ...
}
```

- Pros: One-line fix, matches the existing non-snapshot behavior, prevents runaway tests
- Cons: Some heavy tests (A2.5 URI tests that take 30s) would time out and fail instead of pass

### Option B: Set Timeout in BuildTestExecutor After Engine Creation

In `Test262Test.cs`, after line 215, add:

```csharp
engine.ExecutionTimeout = TimeSpan.FromSeconds(10);
```

This ensures both code paths set the timeout, regardless of whether snapshots are used.

- Pros: Explicit, covers both paths
- Cons: Duplicates timeout logic

### Option C: Increase Timeout to 45 Seconds for Heavy Tests

If the A2.5 URI tests need to pass (they're correct), use a longer timeout:

```csharp
engine.ExecutionTimeout = TimeSpan.FromSeconds(45);
```

Combined with limiting parallel workers:
```xml
<!-- BuiltInsTests.runsettings -->
<RunSettings>
  <NUnit>
    <Where>class =~ BuiltInsTests</Where>
    <NumberOfTestWorkers>4</NumberOfTestWorkers>
  </NUnit>
</RunSettings>
```

- Pros: All tests pass, process stays healthy
- Cons: Slow tests take longer, but they're already running 30s

## Test Plan
- [ ] Apply fix (Option A or B)
- [ ] Run all listed tests individually to confirm they still pass
- [ ] Run full BuiltIns suite: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --settings tests/Asynkron.JsEngine.Tests.Test262/BuiltInsTests.runsettings`
- [ ] Verify no test host crashes with `--blame-crash`
- [ ] Monitor memory: process should stay under 2GB RSS
- [ ] Consider also running: `dotnet test tests/Asynkron.JsEngine.Tests --filter "Category!=Slow"`

## Additional Notes

1. **The listed tests are NOT individually buggy.** They all pass when run alone. The "crash" is a systemic issue of unbounded resource consumption under parallel load.

2. **Collateral damage pattern:** When a test host process dies from OOM, ALL in-flight tests are recorded as failed/crashed, regardless of whether they individually cause the OOM. The user's list likely contains both heavy offenders (URI/parseInt tests) and innocent bystanders (propertyHelper.js tests).

3. **The `CompareArrayPatchScript` probe** (`Reflect.ownKeys(new Intl.Locale('en').getWeekInfo())`) runs for every test and adds non-trivial per-test overhead. Consider caching or removing this probe.

4. **Prior crash fix context:** Commit `87ab5f85` added CancellationToken checks on backward jumps in the IR execution loop, and commit `48decb1e` fixed RegExp property escape crashes. Both fixes require a valid CancellationToken, which only exists when `ExecutionTimeout` is set. The snapshot path bypasses these safety mechanisms by setting `ExecutionTimeout = null`.

5. **Filter sparse array test** (`15.4.4.20-9-c-ii-1.js`): This test creates an array with `srcArr[999999] = -6.6` and then calls `.filter()`, which iterates 1,000,000 indices. Each iteration allocates a string via `ToIndexString(index)`. This is a separate memory concern but is bounded (1M * ~20 bytes = ~20MB).
