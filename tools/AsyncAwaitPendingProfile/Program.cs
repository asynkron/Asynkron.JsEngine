using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    var finalSum = 0;
    function makePromise(val) {
        return new Promise(resolve => resolve(val));
    }
    (async function() {
        let sum = 0;
        for (let i = 0; i < 50000; i++) {
            sum += await makePromise(1);
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
