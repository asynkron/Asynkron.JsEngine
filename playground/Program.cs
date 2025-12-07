using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        try
        {
            var result = await engine.Evaluate("""
// Test: iterator return can call super() when returning early from for-of
var iter = {
  [Symbol.iterator]() {
    return this;
  },
  next() {
    return {done: false};
  },
  return() {
    // Calls |super()|.
    this.f();
    return {done: true};
  },
};

class C extends class {} {
  constructor() {
    iter.f = () => super();

    for (var k of iter) {
      return;
    }
  }
}

try {
  var o = new C();
  console.log('typeof o:', typeof o);
  console.log('Success!');
} catch (e) {
  console.log('Error type:', e.constructor.name);
  console.log('Error message:', e.message);
}
'done';
""");

            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test FAILED: {ex.Message}");
        }
    }
}
