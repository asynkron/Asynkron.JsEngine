using Asynkron.JsEngine;

var engine = new JsEngine();

// Test non-strict mode assignment
try
{
    var script = @"
x = 7;
x;
";
    var result = engine.Evaluate(script).Result;
    Console.WriteLine($"Result: {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
