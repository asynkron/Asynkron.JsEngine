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
// Test yield* - error when getting done property should be catchable
var thrown = new Error('test error');
var badIter = {};
var poisonedDone = Object.defineProperty({}, 'done', {
  get: function() {
    throw thrown;
  }
});
badIter[Symbol.iterator] = function() {
  return {
    next: function() {
      return poisonedDone;
    }
  };
};
function* g() {
  try {
    yield * badIter;
  } catch (err) {
    caught = err;
  }
}
var iter = g();
var result, caught;

result = iter.next();

console.log('caught:', caught);
console.log('caught === thrown:', caught === thrown);
'done';
""");

            Console.WriteLine($"Test completed! Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test FAILED: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}
