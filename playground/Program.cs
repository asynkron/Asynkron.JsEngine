using System;
using System.Threading.Tasks;
using System.IO;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Quick math sanity check
        var logCheck = await engine.Evaluate("""
            var log2 = Math.log(2);
            var x = Math.log(4294967296) / log2;
            [log2, x];
        """) as JsArray;

        Console.WriteLine($"log2 = {logCheck?.Get(0)}");
        Console.WriteLine($"log2(2^32) = {logCheck?.Get(1)}");
    }
}
