using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Test division from Test262 S11.5.2_A3_T1.5
        var code = """
//CHECK#1
if (isNaN({} / function(){return 1}) !== true) {
  throw new Error('#1: {} / function(){return 1} === Not-a-Number. Actual: ' + ({} / function(){return 1}));
}

//CHECK#2
if (isNaN(function(){return 1} / {}) !== true) {
  throw new Error('#2: function(){return 1} / {} === Not-a-Number. Actual: ' + (function(){return 1} / {}));
}

//CHECK#3
if (isNaN(function(){return 1} / function(){return 1}) !== true) {
  throw new Error('#3: function(){return 1} / function(){return 1} === Not-a-Number. Actual: ' + (function(){return 1} / function(){return 1}));
}

//CHECK#4
if (isNaN({} / {}) !== true) {
  throw new Error('#4: {} / {} === Not-a-Number. Actual: ' + ({} / {}));
}

console.log('All tests passed!');
""";

        await engine.Evaluate(code);
    }
}
