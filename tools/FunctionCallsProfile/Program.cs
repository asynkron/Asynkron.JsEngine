using System.Diagnostics;
using Asynkron.JsEngine;

// FunctionCalls benchmark - 20,000 iterations with 4 function calls each = 80,000 calls
var script = """
    'use strict';
    function add(a, b) { return a + b; }
    function mul(a, b) { return a * b; }
    function sub(a, b) { return a - b; }
    function div(a, b) { return a / b; }

    let result = 0;
    for (let i = 0; i < 200000; i++) {
        result = add(result, mul(i, 2));
        result = sub(result, div(i, 2));
    }
    result;
    """;

// Parse once (cached like benchmark does)
var setupEngine = new JsEngine();
var parsed = setupEngine.ParseProgram(script);
await setupEngine.DisposeAsync();

// Warmup
var warmupEngine = new JsEngine();
await warmupEngine.Evaluate(parsed);
await warmupEngine.DisposeAsync();

var sw = Stopwatch.StartNew();
for (var iter = 0; iter < 10; iter++)
{
    // Fresh engine per iteration (like benchmark IterationSetup)
    var engine = new JsEngine();
    await engine.Evaluate(parsed);
    await engine.DisposeAsync();
    Console.Write(".");
}
sw.Stop();
Console.WriteLine($"Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / 10}ms per iteration)");
