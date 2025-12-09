using BenchmarkDotNet.Attributes;

namespace Asynkron.JsEngine.Benchmarks;

/// <summary>
/// Benchmarks for the Evaluator (TypedAstEvaluator) phase.
/// Measures the cost of executing the AST after lexing and parsing.
/// This isolates the evaluation overhead from lexing/parsing.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class EvaluatorBenchmarks
{
    private JsEngine _engine = null!;
    private string _simpleExpression = null!;
    private string _fibonacci = null!;
    private string _loopIntensive = null!;
    private string _objectCreation = null!;
    private string _arrayManipulation = null!;
    private string _stringOperations = null!;
    private string _propertyAccess = null!;
    private string _functionCalls = null!;
    private string _closures = null!;
    private string _recursion = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new JsEngine();
        _engine.ExecutionTimeout = TimeSpan.FromMinutes(5);

        _simpleExpression = """
            let x = 1 + 2 * 3 - 4 / 2;
            let y = x * x;
            let z = y + x;
            z;
            """;

        _fibonacci = """
            function fib(n) {
                if (n <= 1) return n;
                return fib(n - 1) + fib(n - 2);
            }
            fib(20);
            """;

        _loopIntensive = """
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                sum += i;
            }
            sum;
            """;

        _objectCreation = """
            let objects = [];
            for (let i = 0; i < 1000; i++) {
                objects.push({
                    id: i,
                    name: "item" + i,
                    value: i * 2,
                    nested: { a: i, b: i * 2 }
                });
            }
            objects.length;
            """;

        _arrayManipulation = """
            let arr = [];
            for (let i = 0; i < 1000; i++) {
                arr.push(i);
            }
            let mapped = arr.map(x => x * 2);
            let filtered = mapped.filter(x => x > 500);
            let sum = filtered.reduce((a, b) => a + b, 0);
            sum;
            """;

        _stringOperations = """
            let result = "";
            for (let i = 0; i < 500; i++) {
                result += "x";
            }
            let upper = result.toUpperCase();
            let split = result.split("");
            let joined = split.join("-");
            joined.length;
            """;

        _propertyAccess = """
            let obj = {
                a: { b: { c: { d: { e: 1 } } } },
                x: 10,
                y: 20,
                z: 30
            };
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                sum += obj.a.b.c.d.e;
                sum += obj.x + obj.y + obj.z;
            }
            sum;
            """;

        _functionCalls = """
            function add(a, b) { return a + b; }
            function mul(a, b) { return a * b; }
            function sub(a, b) { return a - b; }

            let result = 0;
            for (let i = 0; i < 5000; i++) {
                result = add(result, mul(i, 2));
                result = sub(result, i);
            }
            result;
            """;

        _closures = """
            function makeCounter() {
                let count = 0;
                return function() {
                    return ++count;
                };
            }

            let counters = [];
            for (let i = 0; i < 100; i++) {
                counters.push(makeCounter());
            }

            let sum = 0;
            for (let i = 0; i < 100; i++) {
                for (let j = 0; j < 10; j++) {
                    sum += counters[i]();
                }
            }
            sum;
            """;

        _recursion = """
            function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }

            function sumTo(n) {
                if (n <= 0) return 0;
                return n + sumTo(n - 1);
            }

            let result = 0;
            for (let i = 0; i < 100; i++) {
                result += factorial(10);
                result += sumTo(50);
            }
            result;
            """;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _engine.DisposeAsync();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Create fresh engine for each iteration to avoid state accumulation
        _engine.DisposeAsync().AsTask().Wait();
        _engine = new JsEngine();
        _engine.ExecutionTimeout = TimeSpan.FromMinutes(5);
    }

    [Benchmark(Baseline = true)]
    public async Task<object?> SimpleExpression()
    {
        return await _engine.Evaluate(_simpleExpression);
    }

    [Benchmark]
    public async Task<object?> Fibonacci()
    {
        return await _engine.Evaluate(_fibonacci);
    }

    [Benchmark]
    public async Task<object?> LoopIntensive()
    {
        return await _engine.Evaluate(_loopIntensive);
    }

    [Benchmark]
    public async Task<object?> ObjectCreation()
    {
        return await _engine.Evaluate(_objectCreation);
    }

    [Benchmark]
    public async Task<object?> ArrayManipulation()
    {
        return await _engine.Evaluate(_arrayManipulation);
    }

    [Benchmark]
    public async Task<object?> StringOperations()
    {
        return await _engine.Evaluate(_stringOperations);
    }

    [Benchmark]
    public async Task<object?> PropertyAccess()
    {
        return await _engine.Evaluate(_propertyAccess);
    }

    [Benchmark]
    public async Task<object?> FunctionCalls()
    {
        return await _engine.Evaluate(_functionCalls);
    }

    [Benchmark]
    public async Task<object?> Closures()
    {
        return await _engine.Evaluate(_closures);
    }

    [Benchmark]
    public async Task<object?> Recursion()
    {
        return await _engine.Evaluate(_recursion);
    }
}
