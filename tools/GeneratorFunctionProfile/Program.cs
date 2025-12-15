using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    'use strict';
    function* range(start, end) {
        for (let i = start; i < end; i++) {
            yield i;
        }
    }

    let sum = 0;
    for (let i = 0; i < 1000; i++) {
        for (const n of range(0, 100)) {
            sum += n;
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
