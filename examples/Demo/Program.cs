using Asynkron.JsEngine;
using System;

var engine = new JsEngine();

// Test: Number.MAX_SAFE_INTEGER value
var test = @"
var x = Number.MAX_SAFE_INTEGER;
var str = x.toString();
var xPlus1 = x + 1;
var xPlus1Str = xPlus1.toString();
'MAX_SAFE_INTEGER: ' + str + '\n' +
'MAX_SAFE_INTEGER + 1: ' + xPlus1Str + '\n' +
'literal 9007199254740991: ' + 9007199254740991 + '\n' +
'BigInt(9007199254740991): ' + BigInt(9007199254740991)
";

Console.WriteLine("Test: Number.MAX_SAFE_INTEGER");
try {
    var result = await engine.Evaluate(test).ConfigureAwait(false);
    Console.WriteLine($"  Result:\n{result}");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}

// Direct C# test
Console.WriteLine("\nC# verification:");
Console.WriteLine($"  9007199254740991d: {9007199254740991d}");
Console.WriteLine($"  (long)9007199254740991d: {(long)9007199254740991d}");
Console.WriteLine($"  ((long)9007199254740991d).ToString(): {((long)9007199254740991d).ToString()}");
