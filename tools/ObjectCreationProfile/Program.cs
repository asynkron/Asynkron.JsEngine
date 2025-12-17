using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let objects = [];
    for (let i = 0; i < 10000; i++) {
        objects.push({
            id: i,
            name: "item" + i,
            value: i * 2,
            nested: { a: i, b: i * 2 }
        });
    }
    objects.length;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
