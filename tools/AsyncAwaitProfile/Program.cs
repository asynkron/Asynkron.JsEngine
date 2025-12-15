using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let finalResult = 0;
    async function asyncAdd(a, b) {
        return a + b;
    }
    (async function() {
        let result = 0;
        for (let i = 0; i < 5000; i++) {
            result = await asyncAdd(result, i);
        }
        finalResult = result;
    })();
    finalResult;
    """;

var parsed = engine.ParseProgram(script);
await engine.EvaluateAndAwait(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.EvaluateAndAwait(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
