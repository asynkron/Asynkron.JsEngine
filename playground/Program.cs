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
// Test: super() called in iterator return handler after early return from constructor
// Per spec, the iterator return handler can call super() to initialize this,
// and the constructor should then return the initialized this.
var superCalled = false;
var iter = {
  [Symbol.iterator]() {
    return this;
  },
  next() {
    return {done: false};
  },
  return() {
    console.log('Iterator return called, calling super via arrow function...');
    this.f();
    superCalled = true;
    return {done: true};
  },
};

class C extends class {} {
  constructor() {
    console.log('Constructor entry');
    iter.f = () => {
      console.log('Arrow function calling super()');
      super();
      console.log('super() completed');
    };

    for (var k of iter) {
      console.log('In for-of loop, about to return');
      return;
    }
    console.log('After for-of loop');
  }
}

try {
  console.log('About to create new C()');
  var o = new C();
  console.log('superCalled:', superCalled);
  console.log('typeof o:', typeof o);
  console.log('o:', o);
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
