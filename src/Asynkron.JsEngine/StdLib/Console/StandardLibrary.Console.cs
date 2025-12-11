using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static string FormatConsoleArgs(IReadOnlyList<object?> args)
    {
        var parts = new List<string>();
        foreach (var arg in args)
        {
            if (arg is null)
            {
                parts.Add("null");
            }
            else if (ReferenceEquals(arg, Symbol.Undefined))
            {
                parts.Add("undefined");
            }
            else if (arg is string s)
            {
                parts.Add(s);
            }
            else if (arg is JsObject obj)
            {
                try
                {
                    parts.Add(StringifyValue(obj));
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
                    parts.Add(StringifyValue(arr));
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
