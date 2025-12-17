using Asynkron.JsEngine;

var engine = new JsEngine();
var script = """
    let text = "The quick brown fox jumps over the lazy dog";
    let count = 0;
    for (let i = 0; i < 10000; i++) {
        let matches = text.match(/[a-z]+/gi);
        count += matches ? matches.length : 0;
        let replaced = text.replace(/[aeiou]/g, '*');
        count += replaced.length;
    }
    count;
    """;

var parsed = engine.ParseProgram(script);
await engine.Evaluate(parsed);

for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
Console.WriteLine("Done");
