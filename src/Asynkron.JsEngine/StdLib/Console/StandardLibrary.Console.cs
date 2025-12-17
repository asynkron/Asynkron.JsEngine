using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static string FormatConsoleArgs(IReadOnlyList<JsValue> args)
    {
        var parts = new List<string>();
        foreach (var arg in args)
        {
            if (arg.IsNull)
            {
                parts.Add("null");
            }
            else if (arg.IsUndefined)
            {
                parts.Add("undefined");
            }
            else if (arg.TryGetString(out var s))
            {
                parts.Add(s);
            }
            else if (arg.TryGetObject<JsArray>(out var arr))
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
            else if (arg.TryGetObject<JsObject>(out var obj))
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
            else if (arg.TryGetObject<IJsCallable>(out _))
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
