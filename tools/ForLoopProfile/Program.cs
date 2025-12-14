using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;

// Minimal harness to stress the classic for-loop body without BenchmarkDotNet overhead.
var engine = new JsEngine();
var script = "for (let i = 0, s = 0; i < 1_000_000; i++) { s += i; }";

// Parse once so we time execution + env churn, not parse.
var parsed = engine.ParseProgram(script);

// Warm up once.
await engine.Evaluate(parsed);

// Run multiple times to accumulate allocation samples.
for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
}

Console.WriteLine("Done");
