using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        try
        {
            var result = await engine.Evaluate("1 + 1");
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
