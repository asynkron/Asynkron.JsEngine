using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let result = "";
    for (let i = 0; i < 2000; i++) {
        result += "x";
    }
    let upper = result.toUpperCase();
    let split = result.split("");
    let joined = split.join("-");
    joined.length;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
