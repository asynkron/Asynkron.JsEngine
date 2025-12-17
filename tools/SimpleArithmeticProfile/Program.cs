using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let x = 1 + 2 * 3 - 4 / 2;
    let y = x * x + Math.sqrt(16);
    let z = y % 7 + Math.pow(2, 10);
    z;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 10000; iter++)
{
    await engine.Evaluate(parsed);
}
Console.WriteLine("Done");
