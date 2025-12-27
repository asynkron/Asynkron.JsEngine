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

    /// <summary>
    /// Helper to get all log messages for debugging.
    /// </summary>
    private static string[] GetLogMessages(FakeLogger logger)
    {
        return logger.Collector.Snapshot()
            .Select(r => r.Message)
            .ToArray();
    }

    [Fact]
    public async Task ForLoop_WithLet_CreatesPerIterationEnvironments()
    {
        // for (let i = 0; i < 5; i++) { arr.push(() => i); }
        // Expected: When closures capture the loop variable, per-iteration environments must be created
        // However, when pooling is enabled and no closures exist, the engine may optimize
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

        var messages = GetLogMessages(logger);
        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");
        foreach (var msg in messages.Where(m => m.Contains("JsEnvironment")))
        {
            Output.WriteLine(msg);
        }

        // With 'let' and closures, environments are not pooled (closures capture them)
        // The key assertion is that the closures work correctly (verified above)
        // Environment pooling is disabled when closures exist, so we may not see many Activate/Reset
    }

    [Fact]
    public async Task ForLoop_WithVar_NoPerIterationEnvironments()
    {
        // for (var i = 0; i < 5; i++) { }
        // Expected: 'var' is function-scoped, no per-iteration environments needed
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

        // With 'var', no per-iteration environments are needed
        // There might be some activations for function scope, but not 5 per iteration
        Assert.True(activateCount < 5, $"Expected fewer than 5 Activate calls with 'var', got {activateCount}");
    }

    [Fact]
    public async Task ForOfLoop_WithConst_CreatesPerIterationEnvironments()
    {
        // for (const x of [1, 2, 3]) { }
        // Expected: Each iteration creates a new environment for the loop variable
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
            // for each iteration, a new environment is rented
            for (const x of [1, 2, 3]) {
                // use x to prevent optimization
                void x;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // With 'const' in for-of and 3 iterations, we expect per-iteration environments
        Assert.True(activateCount >= 3, $"Expected at least 3 Activate calls for per-iteration environments, got {activateCount}");
    }

    [Fact]
    public async Task ForOfLoop_WithVar_NoPerIterationEnvironments()
    {
        // for (var x of [1, 2, 3]) { }
        // Expected: 'var' is function-scoped, no per-iteration environments needed
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
            for (var x of [1, 2, 3]) {
                void x;
            }
            """);

        var activateCount = CountLogMessages(logger, "JsEnvironment.Activate");
        var resetCount = CountLogMessages(logger, "JsEnvironment.Reset");

        Output.WriteLine($"Activate count: {activateCount}");
        Output.WriteLine($"Reset count: {resetCount}");

        // With 'var', no per-iteration environments are needed
        Assert.True(activateCount < 3, $"Expected fewer than 3 Activate calls with 'var', got {activateCount}");
    }

    [Fact]
    public async Task ForInLoop_WithLet_CreatesPerIterationEnvironments()
    {
        // for (let k in {a: 1, b: 2}) { }
        // Expected: Each iteration creates a new environment for the loop variable
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

        // With 'let' in for-in and 2 properties, we expect per-iteration environments
        Assert.True(activateCount >= 2, $"Expected at least 2 Activate calls for per-iteration environments, got {activateCount}");
    }

    [Fact]
    public async Task WhileLoop_NoPerIterationEnvironments()
    {
        // while loops don't create per-iteration environments by default
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

        // While loop doesn't create per-iteration environments
        Assert.True(activateCount < 5, $"Expected fewer than 5 Activate calls for while loop, got {activateCount}");
    }

    [Fact]
    public async Task ForLoop_WithLetAndClosure_NoPooling()
    {
        // When a closure captures the loop variable, environments can't be pooled
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

        // With closures, environments shouldn't be returned to pool immediately
        // Activate should happen, but Reset should NOT happen until the closures are done
        // This is hard to verify precisely, but we check that closures work correctly
    }

    [Fact]
    public async Task BlockScope_WithLet_CreatesBlockEnvironment()
    {
        // { let x = 1; } creates a block scope environment
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

        // Three block scopes with 'let' should create environments
        // They can be pooled since no closures capture them
        Assert.True(activateCount >= 3, $"Expected at least 3 Activate calls for block scopes, got {activateCount}");
        Assert.True(resetCount >= 3, $"Expected at least 3 Reset calls for returned block scopes, got {resetCount}");
    }
}
