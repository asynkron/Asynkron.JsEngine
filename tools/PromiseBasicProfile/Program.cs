using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let result = 0;
    for (let i = 0; i < 10000; i++) {
        let p = Promise.resolve(i);
        result += i;
    }
    let chain = Promise.resolve(1)
        .then(x => x + 1)
        .then(x => x + 1)
        .then(x => x + 1);
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
