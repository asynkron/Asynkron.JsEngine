using Asynkron.JsEngine;
using System;

var engine = new JsEngine();

// Test 1: Large integer string conversion (the fix we made)
Console.WriteLine("=== Test 1: Large Integer String Conversion ===");
try {
    var result = await engine.Evaluate(@"
        var a = 9007199254740991;  // Number.MAX_SAFE_INTEGER
        var b = 9007199254740992;  // One above MAX_SAFE_INTEGER (exactly 2^53)
        'a=' + a + ', b=' + b
    ").ConfigureAwait(false);
    Console.WriteLine($"  Result: {result}");
    Console.WriteLine($"  Expected: a=9007199254740991, b=9007199254740992");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}

// Test 2: BigInt vs Number comparison (known issue)
Console.WriteLine("\n=== Test 2: BigInt vs Number Comparison ===");
try {
    var result = await engine.Evaluate("9007199254740993n > 9007199254740992").ConfigureAwait(false);
    Console.WriteLine($"  9007199254740993n > 9007199254740992 = {result}");
    Console.WriteLine($"  Expected: true (known issue: currently returns false)");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}
