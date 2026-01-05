using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        Console.WriteLine("=== Yield* from Catch Test ===\n");

        await engine.Evaluate(@"
function* values() {
  yield 1;
  yield 1;
}
globalThis.dataIterator = values();
globalThis.i = 0;
globalThis.j = 0;
globalThis.k = 0;
globalThis.l = 0;
globalThis.controlIterator = (function*() {
  for (var x of dataIterator) {
    try {
      throw new Error();
    } catch (err) {
      i++;
      yield * values();
      j++;
    }
    k++;
  }
  l++;
})();
");

        Console.WriteLine("Step 1: controlIterator.next()");
        await engine.Evaluate("controlIterator.next()");
        Console.WriteLine($"  Actual:   i={await engine.Evaluate("i")}, j={await engine.Evaluate("j")}, k={await engine.Evaluate("k")}, l={await engine.Evaluate("l")}");
        Console.WriteLine($"  Expected: i=1, j=0, k=0, l=0");
        Console.WriteLine();

        Console.WriteLine("Step 2: controlIterator.next()");
        await engine.Evaluate("controlIterator.next()");
        Console.WriteLine($"  Actual:   i={await engine.Evaluate("i")}, j={await engine.Evaluate("j")}, k={await engine.Evaluate("k")}, l={await engine.Evaluate("l")}");
        Console.WriteLine($"  Expected: i=1, j=0, k=0, l=0");
        Console.WriteLine();

        Console.WriteLine("Step 3: controlIterator.next()");
        await engine.Evaluate("controlIterator.next()");
        Console.WriteLine($"  Actual:   i={await engine.Evaluate("i")}, j={await engine.Evaluate("j")}, k={await engine.Evaluate("k")}, l={await engine.Evaluate("l")}");
        Console.WriteLine($"  Expected: i=2, j=1, k=1, l=0");
        Console.WriteLine();

        Console.WriteLine("Step 4: controlIterator.next()");
        await engine.Evaluate("controlIterator.next()");
        Console.WriteLine($"  Actual:   i={await engine.Evaluate("i")}, j={await engine.Evaluate("j")}, k={await engine.Evaluate("k")}, l={await engine.Evaluate("l")}");
        Console.WriteLine($"  Expected: i=2, j=1, k=1, l=0");
        Console.WriteLine();

        Console.WriteLine("Step 5: controlIterator.next()");
        await engine.Evaluate("controlIterator.next()");
        Console.WriteLine($"  Actual:   i={await engine.Evaluate("i")}, j={await engine.Evaluate("j")}, k={await engine.Evaluate("k")}, l={await engine.Evaluate("l")}");
        Console.WriteLine($"  Expected: i=2, j=2, k=2, l=1");
    }
}
