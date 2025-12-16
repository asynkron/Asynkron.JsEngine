using System.Diagnostics;
using Asynkron.JsEngine;

// Minimal harness to stress the Fibonacci benchmark without BenchmarkDotNet overhead.
var engine = new JsEngine();
var script = """
    'use strict';
    function fib(n) {
        if (n <= 1) return n;
        return fib(n - 1) + fib(n - 2);
    }
    fib(25);
    """;

// Parse once so we time execution, not parse.
var parsed = engine.ParseProgram(script);

// Warm up once.
await engine.Evaluate(parsed);

var sw = Stopwatch.StartNew();
// Run multiple times to accumulate allocation samples.
for (var iter = 0; iter < 10; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
sw.Stop();
Console.WriteLine($"Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / 10}ms per iteration)");
