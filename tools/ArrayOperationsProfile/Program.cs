using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let arr = [];
    for (let i = 0; i < 10000; i++) {
        arr.push(i);
    }
    let mapped = arr.map(x => x * 2);
    let filtered = mapped.filter(x => x > 5000);
    let sum = filtered.reduce((a, b) => a + b, 0);
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
