using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Side-by-side benchmarks for the fast identifier/property access options.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class FastPathBenchmarks
{
    private JsEngine _baseline = null!;
    private JsEngine _fast = null!;

    private string _propertyAccess = null!;
    private string _identifierAccess = null!;

    [GlobalSetup]
    public void Setup()
    {
        _baseline = new JsEngine();
        _fast = new JsEngine(new JsEngineOptions
        {
            EnableFastIdentifierAccess = true,
            EnableFastPropertyAccess = true
        });

        _baseline.ExecutionTimeout = TimeSpan.FromMinutes(5);
        _fast.ExecutionTimeout = TimeSpan.FromMinutes(5);

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
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_baseline is not null)
        {
            await _baseline.DisposeAsync();
        }

        if (_fast is not null)
        {
            await _fast.DisposeAsync();
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<object?> PropertyAccessBaseline()
    {
        return await _baseline.Evaluate(_propertyAccess);
    }

    [Benchmark]
    public async Task<object?> PropertyAccessFast()
    {
        return await _fast.Evaluate(_propertyAccess);
    }

    [Benchmark]
    public async Task<object?> IdentifierAccessBaseline()
    {
        return await _baseline.Evaluate(_identifierAccess);
    }

    [Benchmark]
    public async Task<object?> IdentifierAccessFast()
    {
        return await _fast.Evaluate(_identifierAccess);
    }
}
