using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"""
            const callbacks = [];
            for (let value of [1, 2]) {
                callbacks.push(() => value);
            }
            [callbacks[0](), callbacks[1]()];
        """);

        Console.WriteLine(result);
    }
}
