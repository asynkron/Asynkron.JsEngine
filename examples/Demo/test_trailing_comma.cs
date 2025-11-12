using Asynkron.JsEngine;

var engine = new JsEngine();

var script = """
if (!console) console = {
    trace: () => null,
    log: () => null,
    warn: () => null,
    error: () => null,
    info: () => null,
    debug: () => null,    
};

console.log("Success!");
""";

try
{
    await engine.Evaluate(script);
    Console.WriteLine("Trailing comma test passed!");
}
catch (Exception ex)
{
    Console.WriteLine($"Trailing comma test failed: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
