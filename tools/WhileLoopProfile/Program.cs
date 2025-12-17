using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let sum = 0;
    let i = 0;
    while (i < 100000) {
        sum += i;
        i++;
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
