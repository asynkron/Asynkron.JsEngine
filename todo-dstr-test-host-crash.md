# Investigation Report: Destructuring (dstr) Test262 Tests Crash Test Host

## Problem Summary
Test262 "dstr" (destructuring) category tests across class, function, async-generator, for-of, and assignment contexts reportedly crash the dotnet test host process (StackOverflow or OOM), rather than merely failing. The crash occurs when running large batches of these tests in parallel, not when running individual tests in isolation.

## Affected Components
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.AsyncFunctionInvoker.cs` -- recursive `DriveToCompletion` (lines 96-140)
- `src/Asynkron.JsEngine/Ast/FunctionExpressionExtensions.cs:94` -- `BindFunctionParameters` (parameter destructuring binding)
- `src/Asynkron.JsEngine/Ast/ArrayBindingExtensions.cs:67` -- `BindArrayPattern` (array destructuring with default values)
- `src/Asynkron.JsEngine/Ast/TypedAstEvaluator.ExecutionPlanRunner.Handlers.Declarations.cs:330` -- `HandleComplexVariableDeclaration` (AST fallback for destructuring with defaults)
- `src/Asynkron.JsEngine/Execution/Emitters/DestructuringEmitter.cs:114` -- `IsSimpleArrayBinding` (only handles simple cases; defaults force AST fallback)
- `tests/Asynkron.JsEngine.Tests.Test262/Generated/Tests262Harness.Test262Test.generated.cs:77` -- `RunTestCode` (engine never disposed)

## Evidence Collected

### Test Behavior
- **Individual tests PASS**: `async-gen-meth-ary-ptrn-elem-id-init-skipped.js`, `async-private-gen-meth-ary-ptrn-*` (88 tests), and `func-expr` destructuring tests all pass when run in isolation or small batches.
- **Batch runs consume massive memory**: Running all 14,974 `_dstr` tests simultaneously showed processes consuming 1.3GB+ memory (observed via `ps aux`), with test host processes appearing stuck or unresponsive.
- **No exceptions in profiler**: Exception profiling of representative destructuring patterns (including 1000-iteration loops of async generator destructuring) showed zero exceptions thrown.

### Test Output (Individual runs)
```
Test Run Successful.
Total tests: 88
     Passed: 88
 Total time: 8.2521 Seconds
```

### JS Test Patterns Analyzed
All crashing tests share these characteristics:
1. **Array destructuring in function parameters**: `([x = 23])`, `([w = counter(), x = counter(), y = counter(), z = counter()])`
2. **Default value expressions**: Every test has at least one parameter with a default initializer
3. **Async generator or async function context**: Most involve `async *method(...)` or `async function*(...)`
4. **Promise chain consumption**: All async tests use `.next().then(() => {...}).then($DONE, $DONE)` pattern

### Code Analysis

#### Path 1: Destructuring IR Emission Falls Back to AST
- `DestructuringEmitter.IsSimpleArrayBinding()` (line 114) rejects any binding with default values
- All crash-candidate tests have default values (e.g., `[x = 23]`)
- This forces `HandleComplexVariableDeclaration` (line 330) which calls `EvaluateStatementJsValue` -- AST walking fallback
- AST walking during IR execution means the `BindFunctionParameters` call at `ExecutionPlanRunner.Environment.cs:235` evaluates destructuring defaults through the AST walker, adding significant stack frames

#### Path 2: Recursive DriveToCompletion in AsyncFunctionInvoker
- `AsyncFunctionInvoker.DriveToCompletion()` at line 116 recursively calls itself when `ExecuteAsyncStep` returns `Yield`:
  ```csharp
  case ExecutionPlanRunner.AsyncGeneratorStepKind.Yield:
      DriveToCompletion(ExecutionPlanRunner.ResumeMode.Next, step.Value, resolve, reject);
      break;
  ```
- This is unbounded recursion -- each internal yield adds a stack frame
- There is NO call depth guard on this recursive path (unlike `CallExpressionExtensions.cs:81` which checks `context.CallDepth > context.MaxCallDepth`)
- While `AsyncGeneratorInvoker` does not have this pattern, `AsyncFunctionInvoker` does, and it's used for `async function` calls including the `.then()` callback infrastructure

#### Path 3: Engine Disposal Leak
- `RunTestCode` at `Tests262Harness.Test262Test.generated.cs:77` creates a `JsEngine` via `BuildTestExecutor()` but NEVER disposes it
- With `[Parallelizable(ParallelScope.All)]` and 14,974 dstr tests, hundreds of engines can be simultaneously alive
- Each engine holds: microtask queue, realm state, environment pools, promise chains, all allocated objects
- No `using` statement, no `try/finally`, no explicit `.Dispose()` or `.DisposeAsync()`

#### Path 4: Deep Call Stack from Test Pattern
The combined stack depth for a single async generator destructuring test with defaults:
```
Thread pool thread (1MB-1.5MB stack)
  -> NUnit test runner
    -> RunTestCode
      -> BuildTestExecutor (allocates JsEngine + harness)
      -> ExecuteTestAsync.GetAwaiter().GetResult() (sync-over-async)
        -> engine.Evaluate(program)
          -> ExecuteProgram
            -> ProgramNode AST evaluation
              -> ClassDefinition.CreateClassValue (~15 frames)
                -> CreateFunctionValue (async generator method)
              -> new C() constructor call (~10 frames)
              -> .method([undefined]) invocation
                -> AsyncGeneratorFunctionInvoker -> AsyncGeneratorInvoker
                  -> CreateStepPromise -> new Promise(executor)
                    -> executor -> ExecuteAsyncStep -> ExecutePlan
                      -> EnsureExecutionEnvironment (~10 frames)
                        -> BindFunctionParameters
                          -> ApplyBindingTarget -> BindArrayPattern (~8 frames)
                            -> TryGetIteratorForDestructuring
                            -> [loop: Next, default EvaluateExpression]
                              -> counter() -> SyncFunctionInvoker.Invoke (~8 frames)
                      -> ExecuteInstructionLoop (body)
                        -> HandleStatement -> EvaluateStatementJsValue
                          -> assert.sameValue() -> SyncFunctionInvoker.Invoke (~8 frames)
              -> .next() -> .then() -> PromisePrototype.Then
                -> NewPromiseCapability -> Promise constructor (~6 frames)
                -> PerformThen
              -> .then($DONE, $DONE) -> same pattern
```

Conservative estimate: **~80-100 C# stack frames** for a single test. On a thread pool thread with 1MB stack, this is significant but probably not enough alone to overflow.

However, when combined with:
- Sync-over-async (`GetAwaiter().GetResult()`) holding a stack frame
- NUnit parallel test infrastructure frames
- The `ExecuteStaticBlock` if class has static initializers
- Nested `NewPromiseCapability` calls through the full Promise constructor path

The actual depth could reach **150+ frames**, especially for class-based async generators with private methods.

## Root Cause Analysis

### Hypothesis 1 (Most Likely): OOM from Mass Engine Leak + Parallel Execution
The 14,974 dstr tests run with `[Parallelizable(ParallelScope.All)]` but the `JsEngine` created per test is never disposed. With default NUnit parallelism (CPU core count * 2), many engines accumulate in memory simultaneously. Each engine holds a full realm state, standard library, environment pools, and all test objects. This leads to OOM that crashes the test host process.

- Evidence supporting:
  - `RunTestCode` at line 77 never disposes the engine
  - Process memory observed at 1.3GB+ during batch runs
  - Tests pass individually (single engine, gets GC'd)
  - 14,974 tests is an enormous count for parallel execution
- Evidence against:
  - OOM should produce `OutOfMemoryException`, not necessarily kill the process (though the GC can be overwhelmed)
  - Other test categories with similar count don't crash (need verification)

### Hypothesis 2: Stack Overflow from Deep Call Chains on Thread Pool Threads
The test pattern creates very deep C# call stacks (~80-150 frames). Thread pool threads have 1MB stacks (vs 8MB for main thread on macOS). When many parallel tests execute simultaneously, the thread pool threads can overflow from the combined depth of: NUnit framework -> sync-over-async bridge -> JS engine evaluation -> class creation -> async generator -> promise chains -> destructuring with defaults (AST fallback). The destructuring-with-defaults path is particularly deep because it goes through `HandleComplexVariableDeclaration` -> AST walking -> `BindArrayPattern` -> iterator loop -> expression evaluation -> function calls for each default.

- Evidence supporting:
  - `StackOverflowException` is uncatchable in .NET, terminates the process instantly
  - Thread pool thread stacks are small (1MB)
  - The `DriveToCompletion` recursive pattern in `AsyncFunctionInvoker` has no depth guard
  - Destructuring with defaults forces AST fallback path, adding extra stack frames vs IR path
  - Crash happens in batches (more thread pool contention, smaller stacks) but not in isolation
- Evidence against:
  - Individual tests don't crash even on thread pool threads
  - The recursive `DriveToCompletion` is in `AsyncFunctionInvoker`, not `AsyncGeneratorInvoker`
  - Profiler didn't capture exceptions (but StackOverflow may not be capturable)

### Hypothesis 3: Combination of H1 + H2
OOM pressure from engine leaks reduces available virtual memory, causing the .NET runtime to allocate smaller thread pool stacks or fail to extend stacks, which then triggers StackOverflow on deeply-nested async generator destructuring tests. The two issues compound.

- Evidence supporting: Explains why crash only happens in large batch mode
- Evidence against: Speculative, no direct evidence for this interaction

## Recommended Fix

### Option A: Fix Engine Disposal in Test Harness + Limit Parallelism
Step-by-step:
1. Modify `Tests262Harness.Test262Test.generated.cs:RunTestCode` to dispose the engine:
   ```csharp
   protected void RunTestCode(string test, bool strict)
   {
       var testCase = State.Test262Stream.GetTestFile(test);
       if (strict) testCase = testCase.AsStrict();
       string lastError = null;
       try
       {
           using var executor = BuildTestExecutor(testCase);
           ExecuteTest(executor, testCase);
           // ...
       }
       // ...
   }
   ```
   Note: This is a generated file, so the fix should be applied to the template or generator settings.

2. Add `[LevelOfParallelism(N)]` attribute or runsettings `NUnit.NumberOfTestWorkers` to limit concurrent test count.

- Pros: Addresses the memory leak, reduces memory pressure
- Cons: Generated file may be overwritten; doesn't fix the fundamental depth issue

### Option B: Extend DestructuringEmitter to Handle Default Values (IR Path)
The `DestructuringEmitter.IsSimpleArrayBinding()` rejects patterns with defaults, forcing AST fallback via `HandleComplexVariableDeclaration`. Extending the IR emitter to handle defaults would:
1. Eliminate the AST walking fallback for destructuring with defaults
2. Reduce stack depth significantly (IR instructions execute in a flat loop)
3. Make execution faster overall

- Pros: Fixes root cause of extra stack depth, improves performance
- Cons: Significant implementation effort, risk of new bugs

### Option C: Add Depth Guard to AsyncFunctionInvoker.DriveToCompletion
Add a depth counter to prevent unbounded recursion:
```csharp
private void DriveToCompletion(
    ExecutionPlanRunner.ResumeMode mode,
    JsValue argument,
    IJsCallable resolve,
    IJsCallable reject,
    int depth = 0)
{
    if (depth > 1000)
    {
        AsyncInvokeWithOneArg(reject, (JsValue)"Maximum async function yield depth exceeded");
        return;
    }
    // ... existing code, with recursive call passing depth + 1
}
```

- Pros: Prevents any possible StackOverflow from the recursive yield path
- Cons: Might not be the actual cause for dstr crashes specifically

## Test Plan
- [ ] Fix engine disposal in test harness, run full dstr suite to verify no OOM crash
- [ ] Run with `NUnit.NumberOfTestWorkers=4` to limit parallelism and verify stability
- [ ] Profile memory with full dstr batch to measure per-test memory
- [ ] Test with `--blame-crash` to capture crash dump for definitive root cause: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~_dstr" --settings tests/Asynkron.JsEngine.Tests.Test262/LanguageTests.runsettings --blame-crash`
- [ ] If StackOverflow confirmed: add depth guard to `DriveToCompletion` and consider extending `DestructuringEmitter` for Phase 2
- [ ] After fix: verify individual tests still pass: `dotnet test tests/Asynkron.JsEngine.Tests.Test262 -c Release --filter "FullyQualifiedName~Expressions_class_dstr&FullyQualifiedName~async-private-gen-meth-ary-ptrn"`

## Additional Notes

### Test Count by Category
- Total `_dstr` tests in LanguageTests: **14,974**
- Tests with `async-gen-meth` in dstr: ~500+
- Tests with `ary-ptrn-elem-id-init` (array pattern with defaults): ~300+

### Key Architectural Insight
The `DestructuringEmitter` (Phase 1) only handles "simple" array destructuring -- no defaults, no nested patterns, no rest with nested patterns. This means the majority of dstr Test262 tests fall through to `HandleComplexVariableDeclaration` which calls `EvaluateStatementJsValue` -- an AST walking path that adds significant stack depth. Extending the emitter to Phase 2 (handling defaults) would eliminate this fallback for the most common destructuring patterns in Test262.

### Engine Disposal
The test harness `BuildTestExecutor` at `Test262Test.cs:197` creates engines with `ExecutionTimeout = TimeSpan.FromSeconds(10)` but the returned engine is never disposed. `JsEngine` implements `IAsyncDisposable` and holds significant resources including the event loop channel and microtask queue. With 14,974 parallel tests, this is a critical leak.

### StackOverflowException Behavior in .NET
`StackOverflowException` cannot be caught by `try/catch` in .NET (despite the handler at `JsEngine.cs:2215`). The CLR terminates the process immediately. This means any stack overflow in the test host would appear as an instant process crash with no diagnostic output -- matching the reported behavior exactly.
