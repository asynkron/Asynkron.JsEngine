using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Test division - lexer/parser regex vs division ambiguity
        // The Test262 test failing uses: instance/of/g
        var code = """
var instance = 60;
var of = 6;
var g = 2;

var notRegExp = instance/of/g;

console.log('notRegExp:', notRegExp);
console.log('Expected: 5');
if (notRegExp !== 5) {
    throw new Error('Expected notRegExp to be 5, but got ' + notRegExp);
}
console.log('Test passed!');
""";

        await engine.Evaluate(code);
    }
}
