using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Test named function expression binding immutability
        var code = """
var probeBody, setBody;

var func = function f() {
  probeBody = function() { return f; };
  setBody = function() { f = null; };
};

func();

console.log('probeBody() === func:', probeBody() === func);  // should be true

setBody();  // Try to reassign f to null

console.log('probeBody() === func after setBody():', probeBody() === func);  // should STILL be true (immutable)

if (probeBody() !== func) {
  throw new Error('inner binding is NOT immutable (from body). Expected func, got: ' + probeBody());
}

console.log('All tests passed!');
""";

        await engine.Evaluate(code);
    }
}
