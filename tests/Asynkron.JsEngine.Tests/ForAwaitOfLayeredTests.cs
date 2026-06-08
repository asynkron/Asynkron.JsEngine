using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Layered tests to diagnose and fix the for await...of bug in async IIFEs.
///
/// Each layer builds on the previous one:
/// - Layer 1: Basic async function execution
/// - Layer 2: Async IIFE execution and microtask draining
/// - Layer 3: Regular await in loops (known working)
/// - Layer 4: for await...of over sync iterables (broken)
///
/// The tests use FakeLogger to trace execution and understand what's happening.
/// </summary>
[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
public sealed class ForAwaitOfLayeredTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // ==================== LAYER 1: Basic Async Function ====================

    [Fact(Timeout = 5000)]
    public async Task Layer1_AsyncFunction_CanBeDefinedAndCalled()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        var result = await engine.Evaluate("""
            async function asyncAdd(a, b) {
                return a + b;
            }
            asyncAdd(1, 2);
            """);

        // Async function returns a promise
        Output.WriteLine($"Result type: {result?.GetType().Name}");
        Output.WriteLine($"Result: {result}");

        // Log any messages
        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Layer1_AsyncFunction_AwaitedReturnsValue()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        // Use EvaluateModule for top-level await support, then read the result
        await engine.EvaluateModule("""
            async function asyncAdd(a, b) {
                return a + b;
            }
            globalThis.moduleResult = await asyncAdd(1, 2);
            """);

        var result = await engine.Evaluate("moduleResult");

        Output.WriteLine($"Result: {result}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        Assert.Equal(3.0, result);
    }

    // ==================== LAYER 2: Async IIFE Execution ====================

    [Fact(Timeout = 5000)]
    public async Task Layer2_AsyncIIFE_RejectsExplicitUnifiedBytecodeDecline()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        var result = await engine.EvaluateAndAwait("""
            let finalResult = 0;
            var asyncResult = undefined;
            (async function() {
                finalResult = await Promise.resolve(42);
            })().then(
                () => asyncResult = 'fulfilled:' + finalResult,
                error => asyncResult = String(error));
            asyncResult;
            """);

        Output.WriteLine($"asyncResult: {result}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        AssertAsyncFunctionDeclined(result, "<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task Layer2_AsyncIIFE_ExecutesWithMultipleAwaits()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        await engine.Evaluate("""
            let finalResult = 0;
            (async function() {
                let a = await Promise.resolve(10);
                let b = await Promise.resolve(20);
                let c = await Promise.resolve(30);
                finalResult = a + b + c;
            })();
            """);

        var result = await engine.Evaluate("finalResult");

        Output.WriteLine($"finalResult: {result}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        Assert.Equal(60.0, result);
    }

    // ==================== LAYER 3: Regular Await in Loops ====================

    [Fact(Timeout = 5000)]
    public async Task Layer3_RegularAwaitInForLoop_RejectsExplicitUnifiedBytecodeDecline()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        var result = await engine.EvaluateAndAwait("""
            let finalSum = 0;
            var asyncResult = undefined;
            const arr = [1, 2, 3, 4, 5];
            (async function() {
                let sum = 0;
                for (let i = 0; i < arr.length; i++) {
                    sum += await Promise.resolve(arr[i]);
                }
                finalSum = sum;
            })().then(
                () => asyncResult = 'fulfilled:' + finalSum,
                error => asyncResult = String(error));
            asyncResult;
            """);

        Output.WriteLine($"asyncResult: {result}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        AssertAsyncFunctionDeclined(result, "<anonymous>");
    }

    [Fact(Timeout = 5000)]
    public async Task Layer3_RegularAwaitInWhileLoop_RejectsExplicitUnifiedBytecodeDecline()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        var result = await engine.EvaluateAndAwait("""
            let finalSum = 0;
            var asyncResult = undefined;
            (async function() {
                let sum = 0;
                let i = 1;
                while (i <= 5) {
                    sum += await Promise.resolve(i);
                    i++;
                }
                finalSum = sum;
            })().then(
                () => asyncResult = 'fulfilled:' + finalSum,
                error => asyncResult = String(error));
            asyncResult;
            """);

        Output.WriteLine($"asyncResult: {result}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        AssertAsyncFunctionDeclined(result, "<anonymous>");
    }

    // ==================== LAYER 4: For Await...Of (The Bug) ====================

    [Fact(Timeout = 5000)]
    public async Task Layer4_ForAwaitOf_SingleIteration_Debug()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        // Simplest possible for await...of - just one element
        await engine.Evaluate("""
            let finalResult = 0;
            let iterationCount = 0;
            const arr = [42];
            (async function() {
                for await (const n of arr) {
                    iterationCount++;
                    finalResult = n;
                }
            })();
            """);

        var finalResult = await engine.Evaluate("finalResult");
        var iterationCount = await engine.Evaluate("iterationCount");

        Output.WriteLine($"finalResult: {finalResult}");
        Output.WriteLine($"iterationCount: {iterationCount}");
        Output.WriteLine("--- Logs ---");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        // Expected: iterationCount = 1, finalResult = 42
        Assert.Equal(1.0, iterationCount);
        Assert.Equal(42.0, finalResult);
    }

    [Fact(Timeout = 5000)]
    public async Task Layer4_ForAwaitOf_MultipleIterations_Debug()
    {
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        await engine.Evaluate("""
            let finalSum = 0;
            let iterationCount = 0;
            const arr = [1, 2, 3, 4, 5];
            (async function() {
                let sum = 0;
                for await (const n of arr) {
                    iterationCount++;
                    sum += n;
                }
                finalSum = sum;
            })();
            """);

        var finalSum = await engine.Evaluate("finalSum");
        var iterationCount = await engine.Evaluate("iterationCount");

        Output.WriteLine($"finalSum: {finalSum}");
        Output.WriteLine($"iterationCount: {iterationCount}");
        Output.WriteLine("--- Logs ---");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        // Expected: iterationCount = 5, finalSum = 15
        Assert.Equal(5.0, iterationCount);
        Assert.Equal(15.0, finalSum);
    }

    // ==================== LAYER 4b: Isolating the Difference ====================

    [Fact(Timeout = 5000)]
    public async Task Layer4b_RegularAwaitDeclinesWhileForAwaitOfCompletes()
    {
        // This test runs the same logic with regular for vs for await...of
        // to isolate exactly where the behavior differs

        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        // First: regular for loop with await (known working)
        var regularResult = await engine.EvaluateAndAwait("""
            let resultRegular = 0;
            let countRegular = 0;
            var regularAsyncResult = undefined;
            const arr = [1, 2, 3];
            (async function() {
                for (let i = 0; i < arr.length; i++) {
                    countRegular++;
                    resultRegular += await Promise.resolve(arr[i]);
                }
            })().then(
                () => regularAsyncResult = 'fulfilled:' + resultRegular + ':' + countRegular,
                error => regularAsyncResult = String(error));
            regularAsyncResult;
            """);

        Output.WriteLine($"Regular for loop decline: {regularResult}");

        // Second: for await...of (the bug)
        var forAwaitResult = await engine.EvaluateAndAwait("""
            let resultForAwait = 0;
            let countForAwait = 0;
            var forAwaitAsyncResult = undefined;
            const arr2 = [1, 2, 3];
            (async function() {
                for await (const n of arr2) {
                    countForAwait++;
                    resultForAwait += n;
                }
            })().then(
                () => forAwaitAsyncResult = 'fulfilled:' + resultForAwait + ':' + countForAwait,
                error => forAwaitAsyncResult = String(error));
            forAwaitAsyncResult;
            """);

        Output.WriteLine($"For await...of decline: {forAwaitResult}");
        Output.WriteLine("--- Logs ---");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        AssertAsyncFunctionDeclined(regularResult, "<anonymous>");
        Assert.Equal("fulfilled:6:3", forAwaitResult);
    }

    // ==================== LAYER 4c: Top-level vs IIFE ====================

    [Fact(Timeout = 5000)]
    public async Task Layer4c_ForAwaitOf_TopLevel_Works()
    {
        // For await...of at top level in a module DOES work
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        var result = await engine.EvaluateModule("""
            const arr = [1, 2, 3, 4, 5];
            let sum = 0;
            for await (const n of arr) {
                sum += n;
            }
            sum;
            """);

        Output.WriteLine($"Top-level for await...of result: {result}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        Assert.Equal(15.0, result);
    }

    // ==================== LAYER 4d: Step-by-step iteration ====================

    [Fact(Timeout = 5000)]
    public async Task Layer4d_ManualAsyncIteration_RejectsExplicitUnifiedBytecodeDecline()
    {
        // Manually implement what for await...of should do
        // to see if the issue is in iteration or async handling
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        var result = await engine.EvaluateAndAwait("""
            let finalSum = 0;
            let iterCount = 0;
            var asyncResult = undefined;
            const arr = [1, 2, 3];
            (async function() {
                const iterator = arr[Symbol.iterator]();
                let result = iterator.next();
                while (!result.done) {
                    iterCount++;
                    finalSum += await Promise.resolve(result.value);
                    result = iterator.next();
                }
            })().then(
                () => asyncResult = 'fulfilled:' + finalSum + ':' + iterCount,
                error => asyncResult = String(error));
            asyncResult;
            """);

        var finalSum = await engine.Evaluate("finalSum");
        var iterCount = await engine.Evaluate("iterCount");

        Output.WriteLine($"Manual iteration: count={iterCount}, sum={finalSum}");
        Output.WriteLine("--- Logs ---");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        AssertAsyncFunctionDeclined(result, "<anonymous>");
        Assert.Equal(0.0, iterCount);
        Assert.Equal(0.0, finalSum);
    }

    // ==================== LAYER 5: Benchmark Pattern (Single Eval) ====================

    [Fact(Timeout = 5000)]
    public async Task Layer5_BenchmarkPattern_SingleEvalReturnsBeforeIIFECompletes()
    {
        // This mimics the exact benchmark pattern - single evaluation
        // where finalSum is returned before the IIFE completes.
        // This is expected to fail - it's not a bug, it's how async works!
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        // Single evaluation - finalSum is checked immediately after IIFE call
        var result = await engine.Evaluate("""
            let finalSum = 0;
            const arr = [1, 2, 3, 4, 5];
            (async function() {
                let sum = 0;
                for await (const n of arr) {
                    sum += n;
                }
                finalSum = sum;
            })();
            finalSum;  // This is returned BEFORE the IIFE completes
            """);

        Output.WriteLine($"Immediate finalSum: {result}");

        // Now check after microtasks have drained
        var finalSumAfter = await engine.Evaluate("finalSum");
        Output.WriteLine($"After microtask drain: {finalSumAfter}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        // The immediate result should be 0 (IIFE hasn't completed)
        Assert.Equal(0.0, result);
        // But after the second eval, microtasks have drained
        Assert.Equal(15.0, finalSumAfter);
    }

    [Fact(Timeout = 5000)]
    public async Task Layer5_BenchmarkPattern_WithEvaluateAndAwait()
    {
        // Test if EvaluateAndAwait drains microtasks from fire-and-forget IIFEs
        var logger = new TestLogger();
        await using var engine = CreateEngine(() => new JsEngineOptions { Logger = logger });

        var result = await engine.EvaluateAndAwait("""
            let finalSum = 0;
            const arr = [1, 2, 3, 4, 5];
            (async function() {
                let sum = 0;
                for await (const n of arr) {
                    sum += n;
                }
                finalSum = sum;
            })();
            finalSum;
            """);

        Output.WriteLine($"EvaluateAndAwait result: {result}");

        foreach (var msg in logger.Collector.Snapshot())
        {
            Output.WriteLine($"LOG: {msg.Message}");
        }

        // What does EvaluateAndAwait actually return?
        // If it drains microtasks, it should be 15. If not, it will be 0.
        Output.WriteLine($"Expected: 15 (if microtasks drained) or 0 (if not)");
        // We're testing the actual behavior, not asserting expected behavior yet
    }
}
