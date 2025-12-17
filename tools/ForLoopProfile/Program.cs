using System.Diagnostics;
using Asynkron.JsEngine;

// Minimal harness to stress the classic for-loop body without BenchmarkDotNet overhead.
var engine = new JsEngine();
// Wrap the loop in a function so the benchmark runs in a function scope, enabling slot-based
// locals instead of dictionary-backed globals.
var script = """
'use strict';
function run() {
    let s = 0;
    for (let i = 0; i < 1_000_000; i++) {
        s += i;
    }
    return s;
}
run();
""";

// Parse once so we time execution + env churn, not parse.
var parsed = engine.ParseProgram(script);

// Warm up once.
await engine.Evaluate(parsed);

// Run multiple times to accumulate allocation samples.
var sw = Stopwatch.StartNew();
for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
sw.Stop();

Console.WriteLine($"Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / 20}ms per iteration)");
