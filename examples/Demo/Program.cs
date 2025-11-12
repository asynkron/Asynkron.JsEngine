using Asynkron.JsEngine;

var engine = new JsEngine();

// Test 1: Simple arrow function
var script1 = "var f = (x) => x * 2; console.log(f(5));";
try
{
    engine.Evaluate(script1).Wait();
    Console.WriteLine("Test 1 passed");
}
catch (Exception ex)
{
    Console.WriteLine($"Test 1 failed: {ex.Message}");
}

// Test 2: Arrow function in object literal
var script2 = """
var x = {
    trace: () => null
};
console.log("Test 2 passed");
""";
try
{
    engine.Evaluate(script2).Wait();
}
catch (Exception ex)
{
    Console.WriteLine($"Test 2 failed: {ex.Message}");
}
