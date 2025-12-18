using System.Diagnostics;
using System.Globalization;
using Asynkron.JsEngine;

// Minimal harness to stress async for-of without BenchmarkDotNet overhead.
var runs = 10;
var engine = new JsEngine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Runs={runs}, iterations=5000"));

// Wrap in an async function so the benchmark runs in a function scope
var script = """
'use strict';
async function run() {
    const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    let sum = 0;
    for (let i = 0; i < 5000; i++) {
        for await (const n of arr) {
            sum += n;
        }
    }
    return sum;
}
run();
""";

// Parse once so we time execution, not parse.
var parsed = engine.ParseProgram(script);

// Warm up once.
await engine.EvaluateAndAwait(parsed);

// Run multiple times to accumulate allocation samples.
var sw = Stopwatch.StartNew();
for (var iter = 0; iter < runs; iter++)
{
    await engine.EvaluateAndAwait(parsed);
    Console.Write(".");
}
sw.Stop();

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / runs}ms per iteration)"));
