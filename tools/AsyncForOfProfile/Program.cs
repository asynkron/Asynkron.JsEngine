using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let finalSum = 0;
    const arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    (async function() {
        let sum = 0;
        for (let i = 0; i < 5000; i++) {
            for await (const n of arr) {
                sum += n;
            }
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
