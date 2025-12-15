using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    'use strict';
    function makeCounter() {
        let count = 0;
        return function() {
            return ++count;
        };
    }

    let counters = [];
    for (let i = 0; i < 200; i++) {
        counters.push(makeCounter());
    }

    let sum = 0;
    for (let i = 0; i < 200; i++) {
        for (let j = 0; j < 20; j++) {
            sum += counters[i]();
        }
    }
    sum;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
