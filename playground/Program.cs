using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.JsTypes;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            (function () {
              function assertThrows(f) {
                try { f(); }
                catch (e) { return { caught: true, ctor: e?.constructor?.name, msg: e?.message }; }
                return { caught: false };
              }

              var iterable = {};
              var firstIterResult;
              iterable[Symbol.iterator] = function() {
                var finalIterResult = { value: null, done: true };
                var nextIterResult = firstIterResult;
                return {
                  next: function() {
                    var iterResult = nextIterResult;
                    nextIterResult = finalIterResult;
                    return iterResult;
                  }
                };
              };

              firstIterResult = true;
              return assertThrows(function() {
                for (var x of iterable) {}
              });
            })();
            """);

        if (result is JsArray jsArray)
        {
            foreach (var item in jsArray.Items)
            {
                if (item is JsObject obj)
                {
                    Console.WriteLine("failure:");
                    PrintObject(obj, "  ");
                }
                else
                {
                    Console.WriteLine(item);
                }
            }
        }
        else if (result is System.Collections.IDictionary dict)
        {
            void PrintArray(string label, object? value)
            {
                if (value is JsArray array)
                {
                    Console.WriteLine($"{label}: [{string.Join(",", array.Items.Select(i => i?.ToString() ?? "null"))}]");
                }
            }

            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                Console.WriteLine($"{entry.Key}: {entry.Value}");
                if (entry.Value is System.Collections.IDictionary inner)
                {
                    foreach (System.Collections.DictionaryEntry innerEntry in inner)
                    {
                        Console.WriteLine($"  {innerEntry.Key}: {innerEntry.Value}");
                        PrintArray($"  {innerEntry.Key}", innerEntry.Value);
                    }
                }
            }
        }
    }

    private static void PrintObject(JsObject obj, string indent)
    {
        foreach (var key in obj.Keys)
        {
            var value = obj[key];
            if (value is JsObject nested)
            {
                Console.WriteLine($"{indent}{key}:");
                PrintObject(nested, indent + "  ");
            }
            else
            {
                Console.WriteLine($"{indent}{key}: {value}");
            }
        }
    }
}
