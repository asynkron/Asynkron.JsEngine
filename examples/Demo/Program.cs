using Asynkron.JsEngine;

var engine = new JsEngine();

// Read test script
var script = File.ReadAllText("/tmp/test_oddities.js");

// Run the script
try
{
    engine.EvaluateSync(script);
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
