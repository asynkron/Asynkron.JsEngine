using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            let sum = 0;
            for (let i = 0; i < 5; i++) {
                sum += i;
            }
            sum;
            """);

        Console.WriteLine($"Loop sum: {result}");
    }
}
