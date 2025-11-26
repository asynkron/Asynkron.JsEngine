using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate("""
            var arr = [0, , 2];
            Object.defineProperty(Array.prototype, "2", {
              get() {
                Object.defineProperty(Array.prototype, "1", {
                  get() { return 6.99; },
                  configurable: true
                });
                return 0;
              },
              configurable: true
            });
            [Array.prototype.hasOwnProperty("1"), arr[1], arr.lastIndexOf(6.99)];
        """);

        if (result is JsArray array)
        {
            Console.WriteLine($"proto has 1 -> {array.Get(0)}");
            Console.WriteLine($"arr[1] -> {array.Get(1)}");
            Console.WriteLine($"lastIndexOf -> {array.Get(2)}");
        }
    }
}
