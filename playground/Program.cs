using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        try
        {
            var result = await engine.Evaluate("""
// Test: extending a bound generator function should throw TypeError BEFORE accessing .prototype
var bound = (function* () {}).bind();
Object.defineProperty(bound, "prototype", {
  get: function() {
    throw new Error("FAIL: superclass.prototype should be unreachable");
  },
});

try {
  class C extends bound {}
  console.log('No error thrown - BUG');
} catch (e) {
  console.log('Error type:', e.constructor.name);
  console.log('Error message:', e.message);
}
'done';
""");

            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test FAILED: {ex.Message}");
        }
    }
}
