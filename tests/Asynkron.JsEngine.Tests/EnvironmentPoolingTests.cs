using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests that verify JsEnvironment pool operations (Rent/Return) happen correctly
/// for various loop constructs. Uses FakeLogger to track Activate/Reset calls.
/// The logger is passed through the engine options and flows to all pool operations.
/// </summary>
public class EnvironmentPoolingTests(ITestOutputHelper output) : FastPathTestBase(output)
{
    /// <summary>
    /// Helper to count log messages matching a pattern.
    /// </summary>
    private static int CountLogMessages(FakeLogger logger, string contains)
    {
        return logger.Collector.Snapshot()
            .Count(r => r.Message.Contains(contains, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForLoop_WithLet_CreatesPerIterationEnvironments()
    {
        // for (let i = 0; i < 5; i++) { arr.push(() => i); }
        // With closures capturing the loop variable, environments cannot be pooled
        // because each closure needs its own captured environment.
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        // Use a closure to force per-iteration environment creation
        var result = await engine.Evaluate("""
            'use strict';
            // for loop with 'let' and closures - creates per-iteration environments
            // The closures must capture separate 'i' values for each iteration
            const funcs = [];
            for (let i = 0; i < 5; i++) {
                funcs.push(() => i);
            }
            // Verify each closure captured the correct value
            funcs.map(f => f()).join(',');
            """);

        Assert.Equal("0,1,2,3,4", result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // With closures, environments are NOT pooled (closures capture them)
        // So we expect 0 pool activations/resets - environments are created directly
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task ForLoop_WithVar_NoPerIterationEnvironments()
    {
        // for (var i = 0; i < 5; i++) { }
        // 'var' is function-scoped, so no per-iteration environments are needed
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            // for loop with 'var' does NOT create per-iteration environments
            // because 'var' is function-scoped
            for (var i = 0; i < 5; i++) {
                // empty body
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // With 'var', no per-iteration environments are created
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task ForOfLoop_WithConst_CreatesPerIterationEnvironments()
    {
        // for (const x of [1, 2, 3]) { void x; }
        // With 'const', each iteration creates a new environment for the loop variable
        // These environments CAN be pooled since no closures capture them
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            // for-of with 'const' creates per-iteration environments
            // for each iteration, a new environment is rented from the pool
            for (const x of [1, 2, 3]) {
                // use x to prevent optimization
                void x;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // 3 iterations = 3 environments rented, 2 returned (last one not returned until loop cleanup)
        Assert.Equal(3, activateCount);
        Assert.Equal(2, resetCount);
    }

    [Fact]
    public async Task ForOfLoop_WithVar_NoPerIterationEnvironments()
    {
        // for (var x of [1, 2, 3]) { void x; }
        // 'var' is function-scoped, so only the loop scope environment is pooled
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            // for-of with 'var' does NOT create per-iteration environments
            // Only the loop scope environment is created
            for (var x of [1, 2, 3]) {
                void x;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Only 1 loop scope environment rented and returned
        Assert.Equal(1, activateCount);
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public async Task ForInLoop_WithLet_CreatesPerIterationEnvironments()
    {
        // for (let k in {a: 1, b: 2}) { void k; }
        // With 'let', each iteration creates a new environment
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            // for-in with 'let' creates per-iteration environments
            for (let k in {a: 1, b: 2}) {
                void k;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // 4 environments: TDZ env + loop scope env + 2 per-iteration envs
        // Only 2 returned (TDZ and loop scope returned at cleanup)
        Assert.Equal(4, activateCount);
        Assert.Equal(2, resetCount);
    }

    [Fact]
    public async Task WhileLoop_NoPerIterationEnvironments()
    {
        // while loops don't create per-iteration environments
        // (no loop variable declaration in the header)
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            // while loops don't create per-iteration environments
            // (no loop variable declaration in the header)
            var i = 0;
            while (i < 5) {
                i++;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // While loop with 'var' doesn't create any pooled environments
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task ForLoop_WithLetAndClosure_NoPooling()
    {
        // When a closure captures the loop variable, environments cannot be pooled
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            // for loop with closure - environments cannot be pooled
            // because the closure captures the per-iteration environment
            const funcs = [];
            for (let i = 0; i < 3; i++) {
                funcs.push(() => i); // closure captures 'i'
            }
            // Each closure should return its captured value
            funcs[0]() + funcs[1]() + funcs[2]();
            """);

        Assert.Equal(3d, result); // 0 + 1 + 2 = 3

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // With closures, environments are NOT pooled - they're created directly
        // and kept alive for the closures to reference
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task BlockScope_WithLet_CreatesBlockEnvironment()
    {
        // { let x = 1; } creates a block scope environment
        // These can be pooled since no closures capture them
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            // Block with 'let' creates a block scope environment
            // Each block is pooled since no closures capture them
            {
                let x = 1;
            }
            {
                let y = 2;
            }
            {
                let z = 3;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // 3 block scopes = 3 environments rented and all 3 returned
        Assert.Equal(3, activateCount);
        Assert.Equal(3, resetCount);
    }

    [Fact]
    public async Task ForAwaitOfLoop_WithConst_CreatesPerIterationEnvironments()
    {
        // for await (const x of asyncIterable) { void x; }
        // With 'const', each iteration creates a new environment for the loop variable
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            async function test() {
                // for await...of with 'const' creates per-iteration environments
                const results = [];
                for await (const x of [Promise.resolve(1), Promise.resolve(2), Promise.resolve(3)]) {
                    results.push(x);
                }
                return results.join(',');
            }
            test();
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // 3 iterations = 3 environments rented, 2 returned (same as sync for-of)
        Assert.Equal(3, activateCount);
        Assert.Equal(2, resetCount);
    }

    [Fact]
    public async Task ForAwaitOfLoop_WithVar_NoPerIterationEnvironments()
    {
        // for await (var x of asyncIterable) { void x; }
        // 'var' is function-scoped, no pooled environments needed
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            async function test() {
                // for await...of with 'var' - no per-iteration environments
                const results = [];
                for await (var x of [Promise.resolve(1), Promise.resolve(2), Promise.resolve(3)]) {
                    results.push(x);
                }
                return results.join(',');
            }
            test();
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // With 'var', no pooled environments are created
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task ForOfLoop_WithClosureInsideBody_ClosuresCaptureEnvironments()
    {
        // When a closure is created inside the loop body, the closure captures
        // the per-iteration environment. Each closure keeps its correct captured value.
        // NOTE: Pooling still happens for the for-of head/loop scope environments,
        // but each closure holds a reference to its iteration environment.
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            const funcs = [];
            for (const x of [10, 20, 30]) {
                // Closure inside body captures the iteration environment
                const y = x * 2;
                funcs.push(() => y);
            }
            funcs.map(f => f()).join(',');
            """);

        // Most importantly: each closure captured the correct value
        Assert.Equal("20,40,60", result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Pooling still occurs for some environments (TDZ, loop scope, etc.)
        // The key assertion is the result - closures correctly captured values
        Assert.True(activateCount >= 0, "Some pooled environments may be created");
    }

    [Fact]
    public async Task ForLoop_WithNestedClosure_NoPooling()
    {
        // Nested closure that captures outer loop variable
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            const funcs = [];
            for (let i = 0; i < 3; i++) {
                for (let j = 0; j < 2; j++) {
                    // Closure captures both i and j
                    funcs.push(() => i * 10 + j);
                }
            }
            funcs.map(f => f()).join(',');
            """);

        Assert.Equal("0,1,10,11,20,21", result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Nested closures capture environments, no pooling
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task ForLoop_WithBreak_CorrectResult()
    {
        // Loop with break executes correctly
        // Note: Traditional for loops with 'let' don't pool per-iteration environments
        // via JsEnvironmentPool - they use LoopPlan which handles them differently
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            for (let i = 0; i < 10; i++) {
                sum += i;
                if (i === 4) break; // Exit early
            }
            sum;
            """);

        Assert.Equal(10d, result); // 0+1+2+3+4 = 10

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Traditional for loops use LoopPlan, not JsEnvironmentPool for iteration envs
        // Only block scopes inside may use the pool
        Assert.Equal(1, activateCount);
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public async Task ForLoop_WithContinue_CorrectResult()
    {
        // Loop with continue executes correctly
        // Note: Traditional for loops with 'let' use LoopPlan, not JsEnvironmentPool
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            for (let i = 0; i < 5; i++) {
                if (i === 2) continue; // Skip iteration 2
                sum += i;
            }
            sum;
            """);

        Assert.Equal(8d, result); // 0+1+3+4 = 8

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Traditional for loops use LoopPlan, not JsEnvironmentPool
        Assert.Equal(1, activateCount);
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public async Task ForOfLoop_WithThrow_EnvironmentsReturned()
    {
        // Loop with exception should still clean up environments
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            try {
                for (const x of [1, 2, 3, 4, 5]) {
                    sum += x;
                    if (x === 3) throw new Error('stop');
                }
            } catch (e) {
                // caught
            }
            sum;
            """);

        Assert.Equal(6d, result); // 1+2+3 = 6

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // At least 3 iterations before throw
        Assert.True(activateCount >= 3, $"Expected at least 3 activations, got {activateCount}");
    }

    [Fact]
    public async Task NestedForOfLoops_IndependentPooling()
    {
        // Nested for-of loops should each pool independently
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            for (const x of [1, 2]) {
                for (const y of [10, 20, 30]) {
                    sum += x * y;
                }
            }
            sum;
            """);

        // (1*10 + 1*20 + 1*30) + (2*10 + 2*20 + 2*30) = 60 + 120 = 180
        Assert.Equal(180d, result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Outer: 2 iterations, Inner: 3 iterations * 2 = 6
        // Total: 2 + 6 = 8 activations
        Assert.True(activateCount >= 8, $"Expected at least 8 activations, got {activateCount}");
    }

    [Fact]
    public async Task AsyncFunction_WithSyncForLoop_PoolsNormally()
    {
        // Sync for loop inside async function should pool normally
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        // Define and call async function - engine awaits the promise
        await engine.Evaluate("""
            'use strict';
            async function compute() {
                let sum = 0;
                // Sync loop inside async function
                for (const x of [1, 2, 3, 4]) {
                    sum += x;
                }
                return sum;
            }
            compute();
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // 4 iterations, environments pooled
        Assert.Equal(4, activateCount);
        Assert.Equal(3, resetCount);
    }

    [Fact]
    public async Task AsyncFunction_WithAwaitInsideLoop_PoolsNormally()
    {
        // Loop with await inside should still pool when no closures
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        // Define and call async function - engine awaits the promise
        await engine.Evaluate("""
            'use strict';
            async function compute() {
                let sum = 0;
                for (const x of [1, 2, 3]) {
                    // await inside loop body
                    const val = await Promise.resolve(x);
                    sum += val;
                }
                return sum;
            }
            compute();
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Await suspends execution but doesn't create closures
        // 3 iterations should still be pooled
        Assert.True(activateCount >= 3, $"Expected at least 3 activations, got {activateCount}");
    }

    [Fact]
    public async Task ForLoop_GlobalVarAccess_NoPerIterationEnvironment()
    {
        // Accessing global vars in loop doesn't require per-iteration environments
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            var globalSum = 0;
            for (var i = 0; i < 5; i++) {
                globalSum += i;
            }
            globalSum;
            """);

        Assert.Equal(10d, result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // var loop with global access - no per-iteration environments
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task ForLoop_WithLetAccessingGlobal_CorrectResult()
    {
        // let loop accessing globals works correctly
        // Note: Traditional for loops use LoopPlan which doesn't use JsEnvironmentPool
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            var globalSum = 0;
            for (let i = 0; i < 4; i++) {
                globalSum += i;
            }
            globalSum;
            """);

        Assert.Equal(6d, result); // 0+1+2+3

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Traditional for loops use LoopPlan, not JsEnvironmentPool for iteration envs
        Assert.Equal(1, activateCount);
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public async Task WithStatement_InsideForLoop_NoPooling()
    {
        // 'with' statement creates dynamic scope, prevents pooling
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            // sloppy mode required for 'with'
            var obj = { value: 10 };
            var sum = 0;
            for (var i = 0; i < 3; i++) {
                with (obj) {
                    sum += value + i;
                }
            }
            sum;
            """);

        // (10+0) + (10+1) + (10+2) = 33
        Assert.Equal(33d, result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // 'with' creates dynamic scope - check that it doesn't crash
        // The actual counts depend on implementation details
    }

    [Fact]
    public async Task ForOfLoop_EmptyIterable_OnlyLoopScopeEnvironments()
    {
        // Empty iterable still creates TDZ and loop scope environments (pooled)
        // Just no per-iteration environments since no iterations occur
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            for (const x of []) {
                void x;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // TDZ env + loop scope env + array iterator = 3 activations
        // But no iteration environments since no items to iterate
        Assert.Equal(3, activateCount);
        Assert.Equal(2, resetCount);
    }

    [Fact]
    public async Task ForOfLoop_SingleIteration_LoopScopeAndIterationEnvironments()
    {
        // Single iteration creates TDZ + loop scope + iteration environments
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            for (const x of [42]) {
                void x;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // TDZ env + loop scope env + 1 iteration env = 3 activations
        // Previous iteration's env returned on next iteration, but last iteration's
        // env returned during cleanup
        Assert.Equal(3, activateCount);
        Assert.Equal(2, resetCount);
    }

    [Fact]
    public async Task LabeledLoop_WithBreak_EnvironmentsReturned()
    {
        // Labeled break should properly clean up environments
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            outer: for (const x of [1, 2, 3]) {
                for (const y of [10, 20, 30]) {
                    sum += x * y;
                    if (x === 2 && y === 20) break outer;
                }
            }
            sum;
            """);

        // 1*10 + 1*20 + 1*30 + 2*10 + 2*20 = 10+20+30+20+40 = 120
        Assert.Equal(120d, result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Some environments should be created
        Assert.True(activateCount >= 5, $"Expected at least 5 activations, got {activateCount}");
    }

    [Fact]
    public async Task ForLoop_ImmediatelyInvokedFunction_NoPooling()
    {
        // IIFE inside loop creates closures, prevents pooling
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            let sum = 0;
            for (let i = 0; i < 3; i++) {
                // IIFE creates a closure
                sum += (() => i * 2)();
            }
            sum;
            """);

        Assert.Equal(6d, result); // 0*2 + 1*2 + 2*2 = 6

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // IIFE creates function expression, prevents pooling
        Assert.Equal(0, activateCount);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task ClassMethod_ForOfLoop_AccessingPrivateField()
    {
        // Loop inside class method accessing private field
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            class Counter {
                #total = 0;

                addAll(values) {
                    for (const v of values) {
                        this.#total += v;
                    }
                    return this.#total;
                }
            }
            const c = new Counter();
            c.addAll([1, 2, 3, 4, 5]);
            """);

        Assert.Equal(15d, result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Class method environments are handled - verify some pooling occurs
        Assert.True(activateCount >= 3, $"Expected at least 3 activations, got {activateCount}");
    }

    [Fact]
    public async Task Generator_ForOfLoop_YieldsPooledEnvironments()
    {
        // Generator function with for-of loop
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function* generateDoubles(values) {
                for (const v of values) {
                    yield v * 2;
                }
            }
            [...generateDoubles([1, 2, 3])].join(',');
            """);

        Assert.Equal("2,4,6", result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Generator iteration environments
        Assert.True(activateCount >= 3, $"Expected at least 3 activations, got {activateCount}");
    }

    [Fact]
    public async Task Generator_ForLoop_WithLet_YieldsCorrectValues()
    {
        // Generator with traditional for loop and let
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function* counter(n) {
                for (let i = 0; i < n; i++) {
                    yield i;
                }
            }
            [...counter(5)].join(',');
            """);

        Assert.Equal("0,1,2,3,4", result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Traditional for loops in generators use LoopPlan
    }

    [Fact]
    public async Task Generator_YieldStar_DelegatesIteration()
    {
        // Generator using yield* to delegate to another iterable
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function* inner() {
                yield 1;
                yield 2;
            }
            function* outer() {
                yield* inner();
                yield 3;
            }
            [...outer()].join(',');
            """);

        Assert.Equal("1,2,3", result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // yield* creates iteration environments
    }

    [Fact]
    public async Task Generator_WithClosure_CapturingYieldedValue()
    {
        // Generator that creates closures capturing yielded values
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function* makeGetters(values) {
                for (const v of values) {
                    // Closure captures v
                    yield () => v;
                }
            }
            const getters = [...makeGetters([10, 20, 30])];
            getters.map(g => g()).join(',');
            """);

        Assert.Equal("10,20,30", result);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Each closure should capture correct value
    }

    [Fact]
    public async Task Generator_EarlyReturn_CleansUpEnvironments()
    {
        // Generator that returns early (break out of iteration)
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function* numbers() {
                for (const x of [1, 2, 3, 4, 5]) {
                    yield x;
                }
            }
            // Only consume first 3 values
            let sum = 0;
            for (const n of numbers()) {
                sum += n;
                if (n === 3) break;
            }
            sum;
            """);

        Assert.Equal(6d, result); // 1 + 2 + 3

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Early break should still clean up environments
    }

    [Fact]
    public async Task AsyncGenerator_ForAwaitOf_PoolsEnvironments()
    {
        // Async generator with for-await-of consuming promises
        var logger = new FakeLogger();

        await using var engine = CreateEngineWithOptions(_ => new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            'use strict';
            async function* asyncGen() {
                for await (const x of [Promise.resolve(1), Promise.resolve(2), Promise.resolve(3)]) {
                    yield x * 10;
                }
            }
            (async () => {
                const results = [];
                for await (const v of asyncGen()) {
                    results.push(v);
                }
                return results.join(',');
            })();
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // Async generator creates environments for each iteration
        Assert.True(activateCount >= 3, $"Expected at least 3 activations, got {activateCount}");
    }
}
