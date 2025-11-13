using Asynkron.JsEngine;

var engine = new JsEngine();

// Setup console object with log method
var consoleObj = new JsObject();
consoleObj.Set("log", new Action<object?[]>(args =>
{
    Console.WriteLine(string.Join(" ", args.Select(a => a?.ToString() ?? "null")));
}));
engine.SetGlobalVariable("console", consoleObj);

// Read the test script
var script = File.ReadAllText("/tmp/nbody_debug.js");

try
{
    var result = await engine.Evaluate(script);
    Console.WriteLine($"\n=== Script executed successfully! ===");
    Console.WriteLine($"Result: {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"\n=== Error occurred! ===");
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Type: {ex.GetType().Name}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
}
