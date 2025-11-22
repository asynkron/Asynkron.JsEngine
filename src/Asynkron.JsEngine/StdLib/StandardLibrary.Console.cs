using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateConsoleObject()
    {
        var console = new JsObject();

        // console.log(...args)
        console["log"] = new HostFunction(args =>
        {
            Console.WriteLine(FormatArgs(args));
            return Symbols.Undefined;
        });

        // console.error(...args)
        console["error"] = new HostFunction(args =>
        {
            Console.Error.WriteLine(FormatArgs(args));
            return Symbols.Undefined;
        });

        // console.warn(...args)
        console["warn"] = new HostFunction(args =>
        {
            Console.WriteLine($"Warning: {FormatArgs(args)}");
            return Symbols.Undefined;
        });

        // console.info(...args)
        console["info"] = new HostFunction(args =>
        {
            Console.WriteLine(FormatArgs(args));
            return Symbols.Undefined;
        });

        // console.debug(...args)
        console["debug"] = new HostFunction(args =>
        {
            Console.WriteLine($"Debug: {FormatArgs(args)}");
            return Symbols.Undefined;
        });

        return console;

        // Helper function to format arguments for logging
        static string FormatArgs(IReadOnlyList<object?> args)
        {
            var parts = new List<string>();
            foreach (var arg in args)
            {
                if (arg == null)
                {
                    parts.Add("null");
                }
                else if (ReferenceEquals(arg, Symbols.Undefined))
                {
                    parts.Add("undefined");
                }
                else if (arg is string s)
                {
                    parts.Add(s);
                }
                else if (arg is JsObject obj)
                {
                    // Simple object representation
                    try
                    {
                        parts.Add(StringifyValue(obj, 0));
                    }
                    catch
                    {
                        parts.Add("[object Object]");
                    }
                }
                else if (arg is JsArray arr)
                {
                    try
                    {
                        parts.Add(StringifyValue(arr, 0));
                    }
                    catch
                    {
                        parts.Add("[Array]");
                    }
                }
                else if (arg is IJsCallable)
                {
                    parts.Add("[Function]");
                }
                else
                {
                    parts.Add(JsValueToString(arg));
                }
            }

            return string.Join(" ", parts);
        }
    }
}
