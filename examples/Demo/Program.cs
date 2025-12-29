using Asynkron.JsEngine;

var engine = new JsEngine();
engine.ExecutionTimeout = TimeSpan.FromSeconds(5);

// Test 0: Continue in catch block (the bug we just fixed)
Console.WriteLine("=== Test 0: Continue in Catch Block ===");
try {
    var result = await engine.Evaluate(@"
        var x = 0;
        (function(){
            FOR : for(;;){
                try{
                    x++;
                    if(x===10) return;
                    throw 1;
                } catch(e){
                    continue FOR;
                }
            }
        })();
        x;
    ").ConfigureAwait(false);
    Console.WriteLine($"  Result: {result}");
    Console.WriteLine($"  Expected: 10");
    Console.WriteLine(result?.ToString() == "10" ? "  SUCCESS!" : "  FAILED!");
} catch (OperationCanceledException) {
    Console.WriteLine("  TIMEOUT - Test hangs!");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}

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

// Test 3: Arrow function this binding in strict mode
Console.WriteLine("\n=== Test 3: Arrow function this in strict mode ===");
try {
    var result = await engine.Evaluate(@"
        'use strict';
        var context = 'INITIAL';
        var fnThis = 'NOT SET';
        var fn = function() {
          fnThis = this;  // Capture what 'this' is inside fn
          return () => { context = this; };
        };
        fn()();
        'fnThis=' + (fnThis === undefined ? 'undefined' : typeof fnThis) +
        ', context=' + (context === undefined ? 'undefined' : typeof context + ': ' + context);
    ").ConfigureAwait(false);
    Console.WriteLine($"  Result: {result}");
    Console.WriteLine($"  Expected: fnThis=undefined, context=undefined");
    Console.WriteLine(result?.ToString() == "fnThis=undefined, context=undefined" ? "  SUCCESS!" : "  FAILED!");
} catch (Exception ex) {
    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
}
