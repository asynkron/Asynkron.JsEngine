using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Micro-benchmarks for identifier and property access hot paths.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class FastPathBenchmarks
{
    private JsEngine _engine = null!;

    private string _propertyAccess = null!;
    private string _identifierAccess = null!;
    private string _strictFunctionCalls = null!;
    private string _strictRecursive = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new JsEngine();
        _engine.ExecutionTimeout = TimeSpan.FromMinutes(5);

        _propertyAccess = """
            var obj = { a: { b: { c: { d: { e: 1 } } } }, x: 10, y: 20, z: 30 };
            var sum = 0;
            for (var i = 0; i < 10000; i++) {
                sum += obj.a.b.c.d.e;
                sum += obj.x + obj.y + obj.z;
            }
            sum;
            """;

        _identifierAccess = """
            var x = 1, y = 2, z = 3;
            var sum = 0;
            for (var i = 0; i < 20000; i++) {
                sum += x + y + z;
            }
            sum;
            """;

        // Strict mode function calls - uses fast path with environment pooling
        _strictFunctionCalls = """
            "use strict";
            function add(a, b) { return a + b; }
            function mul(a, b) { return a * b; }
            function sub(a, b) { return a - b; }
            function div(a, b) { return a / b; }

            let result = 0;
            for (let i = 0; i < 50000; i++) {
                result = add(result, mul(i, 2));
                result = sub(result, div(i, 2));
            }
            result;
            """;

        // Strict mode recursive function - tests environment pool growth
        _strictRecursive = """
            "use strict";
            function fib(n) {
                if (n <= 1) return n;
                return fib(n - 1) + fib(n - 2);
            }
            fib(25);
            """;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_engine is not null)
        {
            await _engine.DisposeAsync();
        }
    }

    [Benchmark]
    public async Task<object?> PropertyAccess()
    {
        return await _engine.Evaluate(_propertyAccess);
    }

    [Benchmark]
    public async Task<object?> IdentifierAccess()
    {
        return await _engine.Evaluate(_identifierAccess);
    }

    [Benchmark]
    public async Task<object?> StrictFunctionCalls()
    {
        return await _engine.Evaluate(_strictFunctionCalls);
    }

    [Benchmark]
    public async Task<object?> StrictRecursive()
    {
        return await _engine.Evaluate(_strictRecursive);
    }
}
