#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.JsonHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("console", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ConsolePrototype
{
    /* FLAKY */
    [JsHostMethod("log", Length = 0d)]
    public JsValue Log(IReadOnlyList<JsValue> args)
    {
        Console.WriteLine(FormatConsoleArgs(args));
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("error", Length = 0d)]
    public JsValue Error(IReadOnlyList<JsValue> args)
    {
        Console.Error.WriteLine(FormatConsoleArgs(args));
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("warn", Length = 0d)]
    public JsValue Warn(IReadOnlyList<JsValue> args)
    {
        Console.WriteLine($"Warning: {FormatConsoleArgs(args)}");
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("info", Length = 0d)]
    public JsValue Info(IReadOnlyList<JsValue> args)
    {
        Console.WriteLine(FormatConsoleArgs(args));
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("debug", Length = 0d)]
    public JsValue Debug(IReadOnlyList<JsValue> args)
    {
        Console.WriteLine($"Debug: {FormatConsoleArgs(args)}");
        return JsValue.Undefined;
    }

    private static string FormatConsoleArgs(IReadOnlyList<JsValue> args)
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

        return string.Join(' ', parts);
    }
}
