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
/// Uses FakeLogger to capture debug logs from the engine.
/// </summary>
public class ForOfOptimizationTests
{
    [Fact(Timeout = 5000)]
    public async Task ForOf_SimpleLoop_PoolsEnvironments()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let sum = 0;
                for (const n of [1, 2, 3, 4, 5]) {
                    sum += n;
                }
                return sum;
            }
            run();
            """);

        Assert.Equal(15d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Verify loop environment can be pooled (no closures)
        Assert.Contains(messages, m => m.Contains("CanPoolLoopEnvironment=True", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("CanReuseIterationEnvironment=True", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_WithClosure_DisablesEnvironmentPooling()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let funcs = [];
                for (const n of [1, 2, 3]) {
                    funcs.push(() => n);  // Closure captures iteration environment
                }
                return funcs.map(f => f()).join(',');
            }
            run();
            """);

        Assert.Equal("1,2,3", result);
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Verify closures disable environment reuse
        Assert.Contains(messages, m => m.Contains("CanReuseIterationEnvironment=False", StringComparison.Ordinal));
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

        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let sum = 0;
                for (const n of [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]) {
                    sum += n;
                }
                return sum;
            }
            run();
            """);

        Assert.Equal(55d, JsOps.ToNumber(JsValue.FromObjectUnsafe(result), null));
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Verify fast accumulator path was used
        Assert.Contains(messages, m => m.Contains("Fast accumulator path executed", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_ReusesIterationEnvironment_LogsReuse()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // This pattern should reuse the iteration environment because:
        // - const binding (let/const)
        // - No closures in body
        // - Simple identifier binding
        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let results = [];
                for (const x of [1, 2, 3]) {
                    results.push(x * 2);
                }
                return results.join(',');
            }
            run();
            """);

        Assert.Equal("2,4,6", result);
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Verify iteration environment reuse optimization was triggered
        Assert.Contains(messages, m => m.Contains("Reusing single iteration environment", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOf_ClosureInIterable_CanPoolLoopEnvIsFalse()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        // Closure in iterable captures environment that has loop env in scope chain
        var result = await engine.Evaluate("""
            'use strict';
            var x = 42;
            var probe;
            for (let [_] of [[1, probe = function() { return x; }]]) { }
            probe();
            """);

        Assert.Equal(42d, result);
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();

        // Verify loop environment pooling is disabled due to closure in iterable
        Assert.Contains(messages, m => m.Contains("CanPoolLoopEnvironment=False", StringComparison.Ordinal));
    }
}
