using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var code = @"
class C {
  get x() { return 1; }
}

var desc = Object.getOwnPropertyDescriptor(C.prototype, 'x');
console.log('Getter function:', desc.get);
console.log('Getter own properties:', Object.getOwnPropertyNames(desc.get));
console.log('has prototype:', 'prototype' in desc.get);
console.log('hasOwnProperty:', desc.get.hasOwnProperty('prototype'));
";

        engine.Evaluate(code);
    }
}
