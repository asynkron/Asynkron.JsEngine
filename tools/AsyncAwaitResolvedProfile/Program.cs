using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let finalSum = 0;
    (async function() {
        let sum = 0;
        for (let i = 0; i < 1_000_000; i++) {
            sum += await Promise.resolve(42); //<- are we creating an async bondary here? go to sleep, Promise.resolve is scheduled
        }
        finalSum = sum;
    })();
    finalSum;
    """;

var parsed = engine.ParseProgram(script);
await engine.EvaluateAndAwait(parsed);

for (var iter = 0; iter < 100; iter++)
{
    await engine.EvaluateAndAwait(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
