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
// Test invalid escape sequences in tagged templates
function tag(strs) {
    console.log('strs[0]:', strs[0]);
    console.log('strs[0] === undefined:', strs[0] === undefined);
    console.log('strs.raw[0]:', strs.raw[0]);
}

// Test with incomplete unicode escape
tag`\u{0`;

'done';
""");

            Console.WriteLine($"Test completed! Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test FAILED: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}
