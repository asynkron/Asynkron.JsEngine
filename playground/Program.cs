using System;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        // Test: IteratorClose when destructuring reference throws
        Console.WriteLine("Test: IteratorClose when destructuring reference throws");
        try
        {
            var result = await engine.Evaluate(@"
                var returnCount = 0;
                var nextCount = 0;
                var obj = {};
                var iter = {};
                iter[Symbol.iterator] = function() {
                    return {
                        next: function() {
                            nextCount++;
                            return { value: undefined, done: false };
                        },
                        return: function() {
                            returnCount += 1;
                            return {};
                        }
                    };
                };

                function thrower() {
                    throw new Error('test');
                }

                var err;
                try {
                    // When thrower() throws, iterator.return() should be called
                    for ([ obj[thrower()] ] of [iter]) { }
                } catch (e) {
                    err = e;
                }

                // Per ES spec: reference is evaluated BEFORE next() is called
                // So nextCount=0 (next never called) and returnCount=1 (return called on abrupt)
                'returnCount=' + returnCount + ', nextCount=' + nextCount;
            ");
            Console.WriteLine($"Result: {result}");
            Console.WriteLine("Expected: returnCount=1, nextCount=0 (per ES spec, reference evaluated before next)");
            Console.WriteLine(result?.ToString() == "returnCount=1, nextCount=0" ? "PASS" : "FAIL");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
