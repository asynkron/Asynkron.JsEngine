using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        try
        {
            var result = await engine.Evaluate("""
                'use strict';
                eval('var public = 1;');
                """);
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Eval threw: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
