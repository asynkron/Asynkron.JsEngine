using Asynkron.JsEngine;

var engine = new JsEngine();

// Test 1: Function expression with name (no optional chaining)
try
{
    var result1 = await engine.Evaluate("(function foo() {}).name");
    Console.WriteLine($"Test 1 (no optional): {result1}");
}
catch (Exception ex)
{
    Console.WriteLine($"Test 1 failed: {ex.Message}");
}

// Test 2: Function expression with optional chaining
try
{
    var result2 = await engine.Evaluate("(function bar() {}?.name)");
    Console.WriteLine($"Test 2 (with optional): {result2}");
}
catch (Exception ex)
{
    Console.WriteLine($"Test 2 failed: {ex.Message}");
}

await engine.DisposeAsync();
