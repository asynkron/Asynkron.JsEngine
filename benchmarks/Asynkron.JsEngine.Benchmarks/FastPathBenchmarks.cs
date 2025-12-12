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
}
