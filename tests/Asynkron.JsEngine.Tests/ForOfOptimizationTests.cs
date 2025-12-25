using System;
using System.Linq;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to verify for-of loop optimizations are triggered correctly.
/// Uses FakeLogger to track JsEnvironment allocations via log messages.
/// </summary>
public class ForOfOptimizationTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ForOfOptimizationTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Counts JsEnvironment allocations by counting log messages containing "JsEnvironment allocated".
    /// </summary>
    private static int CountAllocations(FakeLogger logger, int afterIndex = 0)
    {
        return logger.Collector.Snapshot()
            .Skip(afterIndex)
            .Count(r => r.Message.Contains("JsEnvironment allocated", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_SimpleLoop_MinimalEnvironmentAllocations()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Warm up the engine
        await engine.Evaluate("1+1");
        var warmupCount = logger.Collector.Snapshot().Count;

        // For a simple for-of loop with 10 iterations, we should create:
        // - 1 function scope (for 'run')
        // - 1 TDZ environment (for const binding in head) - only if let/const
        // - 1 loop environment
        // - 1 iteration environment (reused) if CanReuseIterationEnvironment is true
        // Total: around 4 environments, NOT 10+
        var result = await engine.Evaluate("""
            function run() {
                let sum = 0;
                for (const n of [1,2,3,4,5,6,7,8,9,10]) {
                    sum += n;
                }
                return sum;
            }
            run();
            """);

        var allocations = CountAllocations(logger, warmupCount);
        _output.WriteLine($"Simple for-of (10 iterations): {allocations} JsEnvironment allocations");
        Assert.Equal(55d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));

        // We should have very few allocations - currently seeing ~4 for a 10-iteration loop
        // If pooling/reuse works correctly: ~4 environments (function, TDZ, loop, iteration reused)
        Assert.True(allocations <= 6,
            $"Expected at most 6 JsEnvironment allocations for simple for-of loop, but got {allocations}. " +
            "Environment pooling/reuse may not be working correctly.");
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_WithClosure_CreatesPerIterationEnvironments()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Warm up
        await engine.Evaluate("1+1");
        var warmupCount = logger.Collector.Snapshot().Count;

        // With closures, each iteration MUST create a new environment
        // because closures capture the iteration-specific bindings
        var result = await engine.Evaluate("""
            function run() {
                let funcs = [];
                for (const n of [1, 2, 3]) {
                    funcs.push(() => n);  // Closure captures iteration environment
                }
                return funcs.map(f => f()).join(',');
            }
            run();
            """);

        var allocations = CountAllocations(logger, warmupCount);
        _output.WriteLine($"With closure (3 iterations): {allocations} JsEnvironment allocations");
        Assert.Equal("1,2,3", result);

        // With closures, we need 1 environment per iteration (3 iterations)
        // Plus function scope, TDZ, loop env, inner function scopes for map callback, etc.
        // Currently seeing ~10 allocations
        Assert.True(allocations >= 5 && allocations <= 15,
            $"Expected 5-15 JsEnvironment allocations when closures exist (need per-iteration envs), but got {allocations}");
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_VarBinding_ReusesLoopEnvironment()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Warm up
        await engine.Evaluate("1+1");
        var warmupCount = logger.Collector.Snapshot().Count;

        // With 'var' binding, the loop variable is in the function scope,
        // not per-iteration scope. Should have minimal allocations.
        var result = await engine.Evaluate("""
            function run() {
                var sum = 0;
                for (var n of [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20]) {
                    sum += n;
                }
                return sum;
            }
            run();
            """);

        var allocations = CountAllocations(logger, warmupCount);
        _output.WriteLine($"Var binding (20 iterations): {allocations} JsEnvironment allocations");
        Assert.Equal(210d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));

        // With 'var', no per-iteration environment is needed
        // Currently seeing ~2 allocations (function scope, loop env)
        Assert.True(allocations <= 4,
            $"Expected at most 4 JsEnvironment allocations for var-based for-of loop (20 iterations), but got {allocations}");
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_100Iterations_PooledEnvironments()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Warm up
        await engine.Evaluate("1+1");
        var warmupCount = logger.Collector.Snapshot().Count;

        // 100 iterations with let/const should still have minimal allocations
        // if environment reuse is working
        var result = await engine.Evaluate("""
            function run() {
                const arr = [];
                for (let i = 0; i < 100; i++) arr.push(i);

                let sum = 0;
                for (const n of arr) {
                    sum += n;
                }
                return sum;
            }
            run();
            """);

        var allocations = CountAllocations(logger, warmupCount);
        _output.WriteLine($"100 iterations with regular for + for-of: {allocations} JsEnvironment allocations");
        Assert.Equal(4950d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));

        // Even with 100 iterations, allocations should be bounded
        // The regular for loop reuses its iteration environment, for-of reuses its own
        // Currently seeing ~7 allocations for 100 iterations
        Assert.True(allocations <= 15,
            $"Expected at most 15 JsEnvironment allocations for 100-iteration for-of loop, but got {allocations}. " +
            "This suggests environment reuse is not working.");
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_AccumulatorPattern_UsesFastPath()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Warm up
        await engine.Evaluate("1+1");
        var warmupCount = logger.Collector.Snapshot().Count;

        var result = await engine.Evaluate("""
            function run() {
                let sum = 0;
                for (const n of [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]) {
                    sum += n;
                }
                return sum;
            }
            run();
            """);

        var allocations = CountAllocations(logger, warmupCount);
        _output.WriteLine($"Fast accumulator path (10 iterations): {allocations} JsEnvironment allocations");
        Assert.Equal(55d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Verify fast accumulator path was used
        Assert.Contains(messages, m => m.Contains("Fast accumulator path executed", StringComparison.Ordinal));

        // Fast path should minimize allocations - currently seeing ~4
        Assert.True(allocations <= 6,
            $"Fast accumulator path should minimize allocations (expected ≤6), but got {allocations}");
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_LogsOptimizationFlags()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        await engine.Evaluate("""
            function run() {
                let sum = 0;
                for (const n of [1, 2, 3]) {
                    sum += n;
                }
                return sum;
            }
            run();
            """);

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Verify optimization flags are logged
        Assert.Contains(messages, m => m.Contains("CanPoolLoopEnvironment=", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("CanReuseIterationEnvironment=", StringComparison.Ordinal));
    }
}
