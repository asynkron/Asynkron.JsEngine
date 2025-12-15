using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    async function* asyncRange(start, end) {
        for (let i = start; i < end; i++) {
            yield i;
        }
    }

    let gen = asyncRange(0, 10);
    let result = gen.next();
    10;
    """;

var parsed = engine.ParseProgram(script);
await engine.EvaluateAndAwait(parsed);

for (var iter = 0; iter < 1000; iter++)
{
    await engine.EvaluateAndAwait(parsed);
}
Console.WriteLine("Done");
