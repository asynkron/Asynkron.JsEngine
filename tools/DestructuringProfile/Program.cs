using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let results = [];
    for (let i = 0; i < 10000; i++) {
        const obj = { a: i, b: i * 2, c: i * 3 };
        const { a, b, c } = obj;
        const arr = [i, i + 1, i + 2];
        const [x, y, z] = arr;
        results.push(a + b + c + x + y + z);
    }
    results.length;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
