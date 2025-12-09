using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var code = @"
// Test generator prototype property
var obj = {
  *method() {}
};

var g = obj.method;
console.log('g.prototype:', g.prototype);
console.log('g has prototype:', 'prototype' in g);
console.log('hasOwnProperty prototype:', Object.prototype.hasOwnProperty.call(g, 'prototype'));
console.log('g.prototype constructor:', g.prototype?.constructor === g);

var desc = Object.getOwnPropertyDescriptor(g, 'prototype');
console.log('descriptor:', desc);
console.log('desc.value:', desc?.value);
console.log('desc.writable:', desc?.writable);
console.log('desc.enumerable:', desc?.enumerable);
console.log('desc.configurable:', desc?.configurable);
";

        engine.Evaluate(code);
    }
}
