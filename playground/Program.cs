using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var script = @"'use strict';
var array = [0, 'foo', , Infinity];
var result = Array.from(array);
console.log('proto equal', Object.getPrototypeOf(result) === Array.prototype);
console.log('result instanceof Array', result instanceof Array);
console.log('result proto type', Object.prototype.toString.call(Object.getPrototypeOf(result)));
JSON.stringify({ protoEqual: Object.getPrototypeOf(result) === Array.prototype, protoType: Object.prototype.toString.call(Object.getPrototypeOf(result)) });
";
        var result = await engine.Evaluate(script);
        Console.WriteLine($"Return value: {result}");
    }
}
