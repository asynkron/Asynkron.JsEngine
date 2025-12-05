using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            (function () {
              var log = '';
              var y = Object.defineProperty({}, Symbol.toPrimitive, {
                get: function() {
                  log += 'get;';
                  throw new Error("boom");
                }
              });

              try {
                return { value: 0 == y, log: log };
              } catch (e) {
                return { threw: true, ctor: e?.constructor?.name, message: e?.message, log: log };
              }
            })();
            """);

        if (result is JsObject obj)
        {
            foreach (var kvp in obj)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
            }
        }
        else
        {
            Console.WriteLine(result ?? "null");
        }
    }
}
