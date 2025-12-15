using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    'use strict';
    function add(a, b) { return a + b; }
    function mul(a, b) { return a * b; }
    function sub(a, b) { return a - b; }
    function div(a, b) { return a / b; }

    let result = 0;
    for (let i = 0; i < 20000; i++) {
        result = add(result, mul(i, 2));
        result = sub(result, div(i, 2));
    }
    result;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
