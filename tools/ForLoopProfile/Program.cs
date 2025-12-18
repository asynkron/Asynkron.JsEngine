using System.Diagnostics;
using System.Globalization;
using Asynkron.JsEngine;
using ForLoopProfile;

// Minimal harness to stress the classic for-loop body without BenchmarkDotNet overhead.
var traceRealm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JSENGINE_TRACE_REALM"));
var runs = traceRealm ? 2 : 20;
var engine = traceRealm
    ? new JsEngine(new JsEngineOptions { Logger = new ConsoleLogger("ForLoopProfile") })
    : new JsEngine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Runs={runs}, iterations=10, traceRealm={traceRealm}"));
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
    var res = await engine.Evaluate(parsed);
    Console.Write(".");
}
sw.Stop();

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / runs}ms per iteration)"));
