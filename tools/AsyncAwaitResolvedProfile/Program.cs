using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    'use strict';
    var finalSum = 0;
    (async function() {
        let sum = 0;
        for (let i = 0; i < 1_00_000; i++) {
            sum += await Promise.resolve(42);
        }
        finalSum = sum;
    })();
    finalSum;
    """;

var parsed = engine.ParseProgram(script);
await engine.EvaluateAndAwait(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.EvaluateAndAwait(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
