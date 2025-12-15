using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let finalSum = 0;
    (async function() {
        let sum = 0;
        for (let i = 0; i < 10000; i++) {
            sum += await Promise.resolve(42);
        }
        finalSum = sum;
    })();
    finalSum;
    """;

var parsed = engine.ParseProgram(script);
await engine.EvaluateAndAwait(parsed);

for (var iter = 0; iter < 10; iter++)
{
    await engine.EvaluateAndAwait(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
