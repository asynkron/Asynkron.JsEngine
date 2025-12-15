using Asynkron.JsEngine;

// Minimal harness to stress the Fibonacci benchmark without BenchmarkDotNet overhead.
var engine = new JsEngine();
var script = """
    function fib(n) {
        if (n <= 1) return n;
        return fib(n - 1) + fib(n - 2);
    }
    fib(20);
    """;

// Parse once so we time execution, not parse.
var parsed = engine.ParseProgram(script);

// Warm up once.
await engine.Evaluate(parsed);

// Run multiple times to accumulate allocation samples.
for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
}

Console.WriteLine("Done");
