using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let obj = {
        a: { b: { c: { d: { e: 1 } } } },
        x: 10,
        y: 20,
        z: 30
    };
    let sum = 0;
    for (let i = 0; i < 50000; i++) {
        sum += obj.a.b.c.d.e;
        sum += obj.x + obj.y + obj.z;
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
