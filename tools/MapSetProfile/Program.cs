using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let map = new Map();
    let set = new Set();
    for (let i = 0; i < 10000; i++) {
        map.set("key" + i, i);
        set.add(i);
    }
    let sum = 0;
    for (let i = 0; i < 10000; i++) {
        if (map.has("key" + i)) {
            sum += map.get("key" + i);
        }
        if (set.has(i)) {
            sum += 1;
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
