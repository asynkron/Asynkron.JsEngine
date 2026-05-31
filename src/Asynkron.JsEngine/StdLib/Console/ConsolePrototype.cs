using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.JsonHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("console", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ConsolePrototype
{
    [JsHostMethod("log", Length = 0d)]
    public JsValue Log(IReadOnlyList<JsValue> args) => WriteConsoleLine(args);

    [JsHostMethod("error", Length = 0d)]
    public JsValue Error(IReadOnlyList<JsValue> args) => WriteConsoleLine(args, toError: true);

    [JsHostMethod("warn", Length = 0d)]
    public JsValue Warn(IReadOnlyList<JsValue> args) => WriteConsoleLine(args, "Warning: ");

    [JsHostMethod("info", Length = 0d)]
    public JsValue Info(IReadOnlyList<JsValue> args) => WriteConsoleLine(args);

    [JsHostMethod("debug", Length = 0d)]
    public JsValue Debug(IReadOnlyList<JsValue> args) => WriteConsoleLine(args, "Debug: ");

    private static JsValue WriteConsoleLine(IReadOnlyList<JsValue> args, string prefix = "", bool toError = false)
    {
        var message = $"{prefix}{FormatConsoleArgs(args)}";
        if (toError)
        {
            Console.Error.WriteLine(message);
        }
        else
        {
            Console.WriteLine(message);
        }

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
            else if (arg.TryGetObject<JsArray>(out _))
            {
                try
                {
                    parts.Add(StringifyValue(arg));
                }
                catch
                {
                    parts.Add("[Array]");
                }
            }
            else if (arg.TryGetObject<JsObject>(out _))
            {
                try
                {
                    parts.Add(StringifyValue(arg));
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
