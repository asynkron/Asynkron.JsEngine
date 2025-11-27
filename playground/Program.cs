using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("Function('-->', '')");

        Console.WriteLine(result ?? "null");
    }
}
